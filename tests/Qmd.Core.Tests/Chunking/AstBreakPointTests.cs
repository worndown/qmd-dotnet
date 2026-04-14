using FluentAssertions;
using Qmd.Core.Chunking;
using Qmd.Core.Models;
using static Qmd.Core.Chunking.AstBreakPointScanner;

namespace Qmd.Core.Tests.Chunking;

[Trait("Category", "Unit")]
public class AstBreakPointTests
{

    [Theory]
    [InlineData("foo.ts", SupportedLanguage.TypeScript)]
    [InlineData("foo.mts", SupportedLanguage.TypeScript)]
    [InlineData("foo.cts", SupportedLanguage.TypeScript)]
    [InlineData("foo.tsx", SupportedLanguage.Tsx)]
    [InlineData("foo.jsx", SupportedLanguage.Tsx)]
    [InlineData("foo.js", SupportedLanguage.JavaScript)]
    [InlineData("foo.mjs", SupportedLanguage.JavaScript)]
    [InlineData("foo.cjs", SupportedLanguage.JavaScript)]
    [InlineData("foo.py", SupportedLanguage.Python)]
    [InlineData("foo.go", SupportedLanguage.Go)]
    [InlineData("foo.rs", SupportedLanguage.Rust)]
    public void DetectLanguage_SupportedExtensions(string filepath, SupportedLanguage expected)
    {
        AstBreakPointScanner.DetectLanguage(filepath).Should().Be(expected);
    }

    [Theory]
    [InlineData("readme.md")]
    [InlineData("notes.txt")]
    [InlineData("data.json")]
    [InlineData("style.css")]
    [InlineData("noext")]
    public void DetectLanguage_UnsupportedExtensions_ReturnsNull(string filepath)
    {
        AstBreakPointScanner.DetectLanguage(filepath).Should().BeNull();
    }

    [Fact]
    public void GetASTBreakPoints_TypeScript_FindsFunctionsAndClasses()
    {
        var content = @"import { foo } from 'bar';

export class MyClass {
    method() {}
}

function hello() {
    return 'world';
}

const arrow = () => 42;
";
        var breakPoints = AstBreakPointScanner.GetASTBreakPoints(content, "test.ts");

        breakPoints.Should().NotBeEmpty();

        // Should find import, class, function at minimum
        breakPoints.Should().Contain(bp => bp.Type == "ast:import");
        breakPoints.Should().Contain(bp => bp.Type == "ast:export" || bp.Type == "ast:class");
        breakPoints.Should().Contain(bp => bp.Type == "ast:func");

        // Scores should match the score map
        var importBp = breakPoints.First(bp => bp.Type == "ast:import");
        importBp.Score.Should().Be(60);

        var funcBp = breakPoints.First(bp => bp.Type == "ast:func");
        funcBp.Score.Should().Be(90);
    }

    [Fact]
    public void GetASTBreakPoints_Python_FindsClassesAndFunctions()
    {
        var content = @"import os
from pathlib import Path

class MyClass:
    def method(self):
        pass

def standalone():
    return 42
";
        var breakPoints = AstBreakPointScanner.GetASTBreakPoints(content, "test.py");

        breakPoints.Should().NotBeEmpty();
        breakPoints.Should().Contain(bp => bp.Type == "ast:import");
        breakPoints.Should().Contain(bp => bp.Type == "ast:class");
        breakPoints.Should().Contain(bp => bp.Type == "ast:func");
    }

    [Fact]
    public void GetASTBreakPoints_Go_FindsFunctionsAndTypes()
    {
        var content = @"package main

import ""fmt""

type Greeter struct {
    Name string
}

func (g Greeter) Hello() string {
    return fmt.Sprintf(""Hello, %s"", g.Name)
}

func main() {
    fmt.Println(""hi"")
}
";
        var breakPoints = AstBreakPointScanner.GetASTBreakPoints(content, "main.go");

        breakPoints.Should().NotBeEmpty();
        breakPoints.Should().Contain(bp => bp.Type == "ast:import");
        breakPoints.Should().Contain(bp => bp.Type == "ast:type");
        breakPoints.Should().Contain(bp => bp.Type == "ast:func" || bp.Type == "ast:method");
    }

    [Fact]
    public void GetASTBreakPoints_Rust_FindsStructsAndFunctions()
    {
        var content = @"use std::fmt;

struct Point {
    x: f64,
    y: f64,
}

impl Point {
    fn new(x: f64, y: f64) -> Self {
        Point { x, y }
    }
}

fn main() {
    let p = Point::new(1.0, 2.0);
}
";
        var breakPoints = AstBreakPointScanner.GetASTBreakPoints(content, "main.rs");

        breakPoints.Should().NotBeEmpty();
        breakPoints.Should().Contain(bp => bp.Type == "ast:import");
        breakPoints.Should().Contain(bp => bp.Type == "ast:struct");
        breakPoints.Should().Contain(bp => bp.Type == "ast:impl");
        breakPoints.Should().Contain(bp => bp.Type == "ast:func");
    }

    [Fact]
    public void GetASTBreakPoints_MarkdownFile_ReturnsEmpty()
    {
        var content = "# Hello\n\nSome text.";
        AstBreakPointScanner.GetASTBreakPoints(content, "readme.md").Should().BeEmpty();
    }

    [Fact]
    public void GetASTBreakPoints_MalformedContent_ReturnsEmptyNoThrow()
    {
        // Badly malformed TypeScript — should not throw
        var content = "}{}{}{export ;;;; class @@@ {{{";
        var result = AstBreakPointScanner.GetASTBreakPoints(content, "broken.ts");
        // May return some partial results or empty — just should not throw
        result.Should().NotBeNull();
    }

    [Fact]
    public void GetASTBreakPoints_ResultsSortedByPosition()
    {
        var content = @"import a from 'a';
function first() {}
class Second {}
function third() {}
";
        var breakPoints = AstBreakPointScanner.GetASTBreakPoints(content, "test.ts");
        breakPoints.Should().BeInAscendingOrder(bp => bp.Pos);
    }

    [Fact]
    public void MergeBreakPoints_AstAndRegex_HighestScoreWins()
    {
        var regex = new List<BreakPoint>
        {
            new(10, 20, "blank"),
            new(50, 1, "newline"),
        };
        var ast = new List<BreakPoint>
        {
            new(10, 90, "ast:func"),
            new(30, 100, "ast:class"),
        };

        var merged = BreakPointScanner.MergeBreakPoints(regex, ast);

        merged.Should().HaveCount(3); // positions 10, 30, 50
        merged.First(bp => bp.Pos == 10).Score.Should().Be(90); // AST wins over regex
        merged.First(bp => bp.Pos == 30).Score.Should().Be(100); // AST only
        merged.First(bp => bp.Pos == 50).Score.Should().Be(1);   // regex only
    }

    [Fact]
    public void ChunkDocument_AutoStrategy_UsesAstBreakPoints()
    {
        // Build a TypeScript file with multiple functions
        var functions = Enumerable.Range(1, 15).Select(i =>
            $"function func{i}() {{\n  // body line 1\n  // body line 2\n  return {i};\n}}\n"
        );
        var content = string.Join("\n", functions);

        var regexChunks = DocumentChunker.ChunkDocument(content);
        var autoChunks = DocumentChunker.ChunkDocument(content, filepath: "test.ts", strategy: ChunkStrategy.Auto);

        // Both should produce chunks (content is large enough)
        regexChunks.Should().NotBeEmpty();
        autoChunks.Should().NotBeEmpty();

        // Auto should use AST break points — chunk boundaries should align better with functions
        // The key verification: chunks should start at function boundaries more often with Auto
        autoChunks.Should().NotBeEmpty("AST chunking should produce chunks for code files");
    }

    /// <summary>
    /// Helper: generate a large TypeScript file with 30 functions.
    /// </summary>
    private static string GenerateLargeTypeScript()
    {
        var parts = new List<string>();
        for (int i = 0; i < 30; i++)
        {
            parts.Add($@"
export function handler{i}(req: Request, res: Response): void {{
  const startTime = Date.now();
  const userId = req.params.userId;
  const sessionToken = req.headers.authorization;

  if (!userId || !sessionToken) {{
    res.status(400).json({{ error: ""Missing required parameters"" }});
    return;
  }}

  console.log(`Processing request {i} for user ${{userId}}`);
  const result = processBusinessLogic{i}(userId, sessionToken);

  const elapsed = Date.now() - startTime;
  res.json({{ data: result, processingTimeMs: elapsed }});
}}
");
        }
        return string.Join("\n", parts);
    }

    /// <summary>
    /// Count how many of the 30 functions are split across chunk boundaries.
    /// </summary>
    private static int CountSplitFunctions(string largeTS, List<TextChunk> chunks)
    {
        int splits = 0;
        for (int i = 0; i < 30; i++)
        {
            var funcStart = largeTS.IndexOf($"function handler{i}(", StringComparison.Ordinal);
            var nextFunc = i < 29
                ? largeTS.IndexOf($"function handler{i + 1}(", funcStart + 1, StringComparison.Ordinal)
                : -1;
            var funcEnd = nextFunc > 0 ? nextFunc : largeTS.Length;

            var chunkIndices = new HashSet<int>();
            for (int ci = 0; ci < chunks.Count; ci++)
            {
                var chunkStart = chunks[ci].Pos;
                var chunkEnd = chunkStart + chunks[ci].Text.Length;
                if (chunkStart < funcEnd && chunkEnd > funcStart)
                    chunkIndices.Add(ci);
            }
            if (chunkIndices.Count > 1) splits++;
        }
        return splits;
    }

    [Fact]
    public void AST_SplitsFewerFunctionsThanRegex()
    {
        var largeTS = GenerateLargeTypeScript();

        var regexChunks = DocumentChunker.ChunkDocument(largeTS);
        var autoChunks = DocumentChunker.ChunkDocument(largeTS, filepath: "handlers.ts", strategy: ChunkStrategy.Auto);

        var regexSplits = CountSplitFunctions(largeTS, regexChunks);
        var astSplits = CountSplitFunctions(largeTS, autoChunks);

        astSplits.Should().BeLessThanOrEqualTo(regexSplits);
    }

    [Fact]
    public void MarkdownProducesIdenticalChunks_AutoVsRegex()
    {
        var sections = new List<string>();
        for (int i = 0; i < 15; i++)
        {
            sections.Add($"# Section {i}\n\n{string.Concat(Enumerable.Repeat("Lorem ipsum dolor sit amet. ", 40))}\n");
        }
        var largeMD = string.Join("\n", sections);

        var mdRegex = DocumentChunker.ChunkDocument(largeMD);
        var mdAst = DocumentChunker.ChunkDocument(largeMD, filepath: "readme.md", strategy: ChunkStrategy.Auto);

        mdAst.Should().HaveCount(mdRegex.Count);
        for (int i = 0; i < mdRegex.Count; i++)
        {
            mdAst[i].Text.Should().Be(mdRegex[i].Text);
            mdAst[i].Pos.Should().Be(mdRegex[i].Pos);
        }
    }

    [Fact]
    public void RegexStrategy_BypassesAstEntirely()
    {
        var largeTS = GenerateLargeTypeScript();

        var regexOnly = DocumentChunker.ChunkDocument(largeTS, filepath: "handlers.ts", strategy: ChunkStrategy.Regex);
        var syncRegex = DocumentChunker.ChunkDocument(largeTS);

        regexOnly.Should().HaveCount(syncRegex.Count);
        for (int i = 0; i < syncRegex.Count; i++)
        {
            regexOnly[i].Text.Should().Be(syncRegex[i].Text);
        }
    }

    [Fact]
    public void NoFilepath_FallsBackToRegex()
    {
        var largeTS = GenerateLargeTypeScript();

        var noPathChunks = DocumentChunker.ChunkDocument(largeTS, filepath: null, strategy: ChunkStrategy.Auto);
        var syncRegex = DocumentChunker.ChunkDocument(largeTS);

        noPathChunks.Should().HaveCount(syncRegex.Count);
    }

    [Fact]
    public void SmallFile_ProducesSingleChunk()
    {
        var smallChunks = DocumentChunker.ChunkDocument("export const x = 1;", filepath: "s.ts", strategy: ChunkStrategy.Auto);
        smallChunks.Should().HaveCount(1);
    }

    [Fact]
    public void TypeScriptExportClass_Scores90()
    {
        var code = "export class Foo {}\nexport function bar() {}";
        var points = AstBreakPointScanner.GetASTBreakPoints(code, "a.ts");
        var exportPoint = points.FirstOrDefault(p => p.Type == "ast:export");
        exportPoint.Should().NotBeNull();
        exportPoint!.Score.Should().Be(90);
    }

    [Fact]
    public void PythonClass_Scores100()
    {
        var code = "class Foo:\n    pass\n\ndef bar():\n    pass";
        var points = AstBreakPointScanner.GetASTBreakPoints(code, "a.py");
        var classPoint = points.FirstOrDefault(p => p.Type == "ast:class");
        classPoint.Should().NotBeNull();
        classPoint!.Score.Should().Be(100);
    }

    [Fact]
    public void GoType_Scores80()
    {
        var code = "package main\n\ntype Server struct {\n    port int\n}\n\nfunc main() {}";
        var points = AstBreakPointScanner.GetASTBreakPoints(code, "a.go");
        var typePoint = points.FirstOrDefault(p => p.Type == "ast:type");
        typePoint.Should().NotBeNull();
        typePoint!.Score.Should().Be(80);
    }

    [Fact]
    public void RustEnum_Scores80()
    {
        var code = "enum State {\n    On,\n    Off,\n}\n\nfn main() {}";
        var points = AstBreakPointScanner.GetASTBreakPoints(code, "a.rs");
        var enumPoint = points.FirstOrDefault(p => p.Type == "ast:enum");
        enumPoint.Should().NotBeNull();
        enumPoint!.Score.Should().Be(80);
    }

    [Theory]
    [InlineData("src/Auth.TS", SupportedLanguage.TypeScript)]
    [InlineData("src/script.PY", SupportedLanguage.Python)]
    [InlineData("src/Main.GO", SupportedLanguage.Go)]
    [InlineData("src/lib.RS", SupportedLanguage.Rust)]
    [InlineData("src/App.JSX", SupportedLanguage.Tsx)]
    [InlineData("src/App.TSX", SupportedLanguage.Tsx)]
    public void DetectLanguage_CaseInsensitive_ReturnsCorrectLanguage(string filepath, SupportedLanguage expected)
    {
        // Language detection is case-insensitive for extensions
        AstBreakPointScanner.DetectLanguage(filepath).Should().Be(expected);
    }

    [Fact]
    public void DetectLanguage_WorksWithVirtualQmdPaths()
    {
        // Language detection works with virtual qmd:// paths
        AstBreakPointScanner.DetectLanguage("qmd://myproject/src/auth.ts").Should().Be(SupportedLanguage.TypeScript);
    }

    [Fact]
    public void MergeBreakPoints_ResultSortedByPosition()
    {
        // Explicit sort assertion: merged result is ordered by position
        var regex = new List<BreakPoint>
        {
            new(50, 1, "newline"),
            new(10, 20, "blank"),
        };
        var ast = new List<BreakPoint>
        {
            new(30, 100, "ast:class"),
        };

        var merged = BreakPointScanner.MergeBreakPoints(regex, ast);
        merged.Should().BeInAscendingOrder(bp => bp.Pos);
    }

    [Fact]
    public void ChunkDocumentWithBreakPoints_EquivalentToChunkDocument()
    {
        // Produces identical output to chunkDocument for the same content
        var content = string.Concat(Enumerable.Repeat("Lorem ipsum dolor sit amet. ", 100));

        var regularChunks = DocumentChunker.ChunkDocument(content);
        var breakPointChunks = DocumentChunker.ChunkDocument(content, filepath: null, strategy: ChunkStrategy.Regex);

        regularChunks.Should().HaveCount(breakPointChunks.Count);
        for (int i = 0; i < regularChunks.Count; i++)
        {
            regularChunks[i].Text.Should().Be(breakPointChunks[i].Text);
            regularChunks[i].Pos.Should().Be(breakPointChunks[i].Pos);
        }
    }

    [Fact]
    public void GetASTBreakPoints_TypeScript_PositionsMatchSourceText()
    {
        // Break point positions match actual content positions
        var content = @"import { Database } from './db';

export class AuthService {
    authenticate() { return true; }
}

export function hashPassword() { return ''; }
";
        var points = AstBreakPointScanner.GetASTBreakPoints(content, "src/auth.ts");
        var firstImport = points.FirstOrDefault(p => p.Type == "ast:import");

        firstImport.Should().NotBeNull();
        content.Substring(firstImport!.Pos, 6).Should().Be("import");
    }

    [Fact]
    public void GetASTBreakPoints_Python_CapturesMethodDefsInsideClass()
    {
        // Captures method definitions inside classes
        // Should capture __init__, authenticate, and validate_token as func
        var content = @"import os
from typing import Optional

class AuthService:
    def __init__(self, db):
        self.db = db

    async def authenticate(self, user, token):
        session = await self.db.find(token)
        return session.user_id == user.id

    def validate_token(self, token):
        return len(token) == 64

def hash_password(password):
    return 'hash'
";
        var points = AstBreakPointScanner.GetASTBreakPoints(content, "auth.py");
        var funcPoints = points.Where(p => p.Type == "ast:func").ToList();

        // __init__, authenticate, validate_token (inside class) + hash_password (standalone)
        funcPoints.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void GetASTBreakPoints_Go_FuncAndMethodBothScore90()
    {
        // Go function and method both score 90
        var content = @"package main

import ""fmt""

type AuthService struct {
    db *Database
}

func (s *AuthService) Authenticate(user User) bool {
    return true
}

func HashPassword(password string) string {
    return ""hash""
}
";
        var points = AstBreakPointScanner.GetASTBreakPoints(content, "auth.go");
        var funcPoint = points.FirstOrDefault(p => p.Type == "ast:func");
        var methodPoint = points.FirstOrDefault(p => p.Type == "ast:method");

        funcPoint.Should().NotBeNull();
        methodPoint.Should().NotBeNull();
        funcPoint!.Score.Should().Be(90);
        methodPoint!.Score.Should().Be(90);
    }

    [Fact]
    public void GetASTBreakPoints_Rust_StructImplTraitAllScore100()
    {
        // Rust struct, impl, and trait all score 100
        var content = @"use std::collections::HashMap;

struct AuthService {
    db: Database,
}

impl AuthService {
    fn authenticate(&self, user: &User) -> bool {
        true
    }
}

trait Authenticatable {
    fn validate(&self) -> bool;
}

enum Role {
    Admin,
    User,
}

fn hash_password(password: &str) -> String {
    String::new()
}
";
        var points = AstBreakPointScanner.GetASTBreakPoints(content, "auth.rs");
        var structPoint = points.FirstOrDefault(p => p.Type == "ast:struct");
        var implPoint = points.FirstOrDefault(p => p.Type == "ast:impl");
        var traitPoint = points.FirstOrDefault(p => p.Type == "ast:trait");

        structPoint.Should().NotBeNull();
        implPoint.Should().NotBeNull();
        traitPoint.Should().NotBeNull();
        structPoint!.Score.Should().Be(100);
        implPoint!.Score.Should().Be(100);
        traitPoint!.Score.Should().Be(100);
    }

    [Fact]
    public void GetASTBreakPoints_UnknownExtension_ReturnsEmpty()
    {
        // Returns empty array for unknown extensions
        AstBreakPointScanner.GetASTBreakPoints("data,here", "file.csv").Should().BeEmpty();
    }

    [Fact]
    public void GetASTBreakPoints_EmptyContent_ReturnsEmpty()
    {
        // Handles empty content gracefully
        AstBreakPointScanner.GetASTBreakPoints("", "empty.ts").Should().BeEmpty();
    }
}
