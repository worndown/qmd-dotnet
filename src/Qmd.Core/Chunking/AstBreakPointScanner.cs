using System.Collections.Concurrent;
using Qmd.Core.Models;
using TreeSitter;

namespace Qmd.Core.Chunking;

/// <summary>
/// AST-aware break point extraction via tree-sitter.
/// Ports getASTBreakPoints() from src/ast.ts.
/// Degrades gracefully: unsupported languages or parse failures return empty lists.
/// </summary>
public static class AstBreakPointScanner
{
    // =========================================================================
    // Language Detection
    // =========================================================================

    public enum SupportedLanguage { TypeScript, Tsx, JavaScript, Python, Go, Rust }

    private static readonly Dictionary<string, SupportedLanguage> ExtensionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        [".ts"] = SupportedLanguage.TypeScript,
        [".mts"] = SupportedLanguage.TypeScript,
        [".cts"] = SupportedLanguage.TypeScript,
        [".tsx"] = SupportedLanguage.Tsx,
        [".jsx"] = SupportedLanguage.Tsx,
        [".js"] = SupportedLanguage.JavaScript,
        [".mjs"] = SupportedLanguage.JavaScript,
        [".cjs"] = SupportedLanguage.JavaScript,
        [".py"] = SupportedLanguage.Python,
        [".go"] = SupportedLanguage.Go,
        [".rs"] = SupportedLanguage.Rust,
    };

    public static SupportedLanguage? DetectLanguage(string filepath)
    {
        var ext = Path.GetExtension(filepath);
        if (string.IsNullOrEmpty(ext)) return null;
        return ExtensionMap.TryGetValue(ext, out var lang) ? lang : null;
    }

    // =========================================================================
    // Grammar Name Mapping
    // =========================================================================

    private static readonly Dictionary<SupportedLanguage, string> GrammarNames = new()
    {
        [SupportedLanguage.TypeScript] = "TypeScript",
        [SupportedLanguage.Tsx] = "Tsx",
        [SupportedLanguage.JavaScript] = "JavaScript",
        [SupportedLanguage.Python] = "Python",
        [SupportedLanguage.Go] = "Go",
        [SupportedLanguage.Rust] = "Rust",
    };

    // =========================================================================
    // Per-Language S-Expression Queries (verbatim from src/ast.ts:87-144)
    // =========================================================================

    private static readonly Dictionary<SupportedLanguage, string> LanguageQueries = new()
    {
        [SupportedLanguage.TypeScript] = @"
            (export_statement) @export
            (class_declaration) @class
            (function_declaration) @func
            (method_definition) @method
            (interface_declaration) @iface
            (type_alias_declaration) @type
            (enum_declaration) @enum
            (import_statement) @import
            (lexical_declaration (variable_declarator value: (arrow_function))) @func
            (lexical_declaration (variable_declarator value: (function_expression))) @func
        ",
        [SupportedLanguage.Tsx] = @"
            (export_statement) @export
            (class_declaration) @class
            (function_declaration) @func
            (method_definition) @method
            (interface_declaration) @iface
            (type_alias_declaration) @type
            (enum_declaration) @enum
            (import_statement) @import
            (lexical_declaration (variable_declarator value: (arrow_function))) @func
            (lexical_declaration (variable_declarator value: (function_expression))) @func
        ",
        [SupportedLanguage.JavaScript] = @"
            (export_statement) @export
            (class_declaration) @class
            (function_declaration) @func
            (method_definition) @method
            (import_statement) @import
            (lexical_declaration (variable_declarator value: (arrow_function))) @func
            (lexical_declaration (variable_declarator value: (function_expression))) @func
        ",
        [SupportedLanguage.Python] = @"
            (class_definition) @class
            (function_definition) @func
            (decorated_definition) @decorated
            (import_statement) @import
            (import_from_statement) @import
        ",
        [SupportedLanguage.Go] = @"
            (type_declaration) @type
            (function_declaration) @func
            (method_declaration) @method
            (import_declaration) @import
        ",
        [SupportedLanguage.Rust] = @"
            (struct_item) @struct
            (impl_item) @impl
            (function_item) @func
            (trait_item) @trait
            (enum_item) @enum
            (use_declaration) @import
            (type_item) @type
            (mod_item) @mod
        ",
    };

    // =========================================================================
    // Score Map (from src/ast.ts:151-165)
    // =========================================================================

    private static readonly Dictionary<string, double> ScoreMap = new()
    {
        ["class"] = 100,
        ["iface"] = 100,
        ["struct"] = 100,
        ["trait"] = 100,
        ["impl"] = 100,
        ["mod"] = 100,
        ["export"] = 90,
        ["func"] = 90,
        ["method"] = 90,
        ["decorated"] = 90,
        ["type"] = 80,
        ["enum"] = 80,
        ["import"] = 60,
    };

    // =========================================================================
    // Grammar & Query Caching
    // =========================================================================

    private static readonly ConcurrentDictionary<SupportedLanguage, Language> LanguageCache = new();
    private static readonly ConcurrentDictionary<SupportedLanguage, Query> QueryCache = new();
    private static readonly ConcurrentDictionary<SupportedLanguage, bool> FailedLanguages = new();

    private static Language GetOrCreateLanguage(SupportedLanguage lang)
    {
        return LanguageCache.GetOrAdd(lang, l => new Language(GrammarNames[l]));
    }

    private static Query GetOrCreateQuery(SupportedLanguage lang, Language language)
    {
        return QueryCache.GetOrAdd(lang, l => new Query(language, LanguageQueries[l]));
    }

    // =========================================================================
    // AST Break Point Extraction
    // =========================================================================

    /// <summary>
    /// Parse a source file and return break points at AST node boundaries.
    /// Returns empty list for unsupported languages, parse failures, or grammar loading failures.
    /// </summary>
    public static List<BreakPoint> GetASTBreakPoints(string content, string filepath)
    {
        var lang = DetectLanguage(filepath);
        if (lang == null) return [];

        if (FailedLanguages.ContainsKey(lang.Value)) return [];

        try
        {
            var language = GetOrCreateLanguage(lang.Value);
            var query = GetOrCreateQuery(lang.Value, language);

            using var parser = new Parser(language);
            using var tree = parser.Parse(content);
            if (tree == null) return [];

            using var cursor = query.Execute(tree.RootNode);

            // Deduplicate: at each position, keep the highest-scoring capture
            var seen = new Dictionary<int, BreakPoint>();

            foreach (var capture in cursor.Captures)
            {
                var pos = capture.Node.StartIndex;
                var score = ScoreMap.GetValueOrDefault(capture.Name, 20);
                var type = $"ast:{capture.Name}";

                if (!seen.TryGetValue(pos, out var existing) || score > existing.Score)
                {
                    seen[pos] = new BreakPoint(pos, score, type);
                }
            }

            var result = seen.Values.ToList();
            result.Sort((a, b) => a.Pos.CompareTo(b.Pos));
            return result;
        }
        catch (Exception ex)
        {
            FailedLanguages.TryAdd(lang.Value, true);
            Console.Error.WriteLine($"[qmd] AST parse failed for {filepath}, falling back to regex: {ex.Message}");
            return [];
        }
    }
}
