using System.Text.RegularExpressions;

namespace Qmd.Core.Configuration;

/// <summary>
/// Manages QMD configuration: collections, contexts, config path resolution.
/// </summary>
public class ConfigManager
{
    private static readonly Regex ValidNameRegex = new(@"^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

    private IConfigSource _source;
    private string _indexName = "index";

    public ConfigManager(IConfigSource? source = null)
    {
        _source = source ?? new FileConfigSource(GetDefaultConfigFilePath());
    }

    // =========================================================================
    // Config path resolution
    // =========================================================================

    public void SetIndexName(string name)
    {
        if (name.Contains('/') || name.Contains('\\'))
        {
            // Resolve relative paths to absolute, replace separators with _, strip leading _ 
            var resolved = Path.GetFullPath(name);
            _indexName = resolved
                .Replace("/", "_")
                .Replace("\\", "_")
                .TrimStart('_');
        }
        else
        {
            _indexName = name;
        }

        if (_source is FileConfigSource)
        {
            var newPath = Path.Combine(GetConfigDir(), $"{_indexName}.yml");
            _source = new FileConfigSource(newPath);
        }
    }

    public static string GetConfigDir()
    {
        var envDir = Environment.GetEnvironmentVariable("QMD_CONFIG_DIR");
        if (!string.IsNullOrEmpty(envDir)) return envDir;

        // XDG_CONFIG_HOME support
        var xdgConfig = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(xdgConfig)) return Path.Combine(xdgConfig, "qmd");

        // On Windows, use LocalApplicationData to keep config alongside the database.
        // On Linux/macOS, ApplicationData already maps to ~/.config (XDG default).
        var folder = OperatingSystem.IsWindows()
            ? Environment.SpecialFolder.LocalApplicationData
            : Environment.SpecialFolder.ApplicationData;
        return Path.Combine(Environment.GetFolderPath(folder), "qmd");
    }

    public string GetConfigFilePath()
    {
        if (_source is FileConfigSource fcs) return fcs.FilePath;
        return Path.Combine(GetConfigDir(), $"{_indexName}.yml");
    }

    public string GetConfigPath() => _source.DisplayPath;
    public bool ConfigExists() => _source.Exists;

    public void SetConfigSource(IConfigSource source) => _source = source;

    // =========================================================================
    // Core I/O
    // =========================================================================

    public CollectionConfig LoadConfig() => _source.Load();

    public void SaveConfig(CollectionConfig config) => _source.Save(config);

    // =========================================================================
    // Collection operations
    // =========================================================================

    public NamedCollection? GetCollection(string name)
    {
        var config = LoadConfig();
        if (!config.Collections.TryGetValue(name, out var coll)) return null;
        return ToNamed(name, coll);
    }

    public List<NamedCollection> ListCollections()
    {
        var config = LoadConfig();
        return config.Collections.Select(kv => ToNamed(kv.Key, kv.Value)).ToList();
    }

    public List<NamedCollection> GetDefaultCollections()
    {
        return ListCollections().Where(c => c.IncludeByDefault != false).ToList();
    }

    public List<string> GetDefaultCollectionNames()
    {
        return GetDefaultCollections().Select(c => c.Name).ToList();
    }

    public void AddCollection(string name, string path, string pattern = "**/*.md")
    {
        var config = LoadConfig();
        if (config.Collections.TryGetValue(name, out var existing))
        {
            existing.Path = path;
            existing.Pattern = pattern;
        }
        else
        {
            config.Collections[name] = new Collection { Path = path, Pattern = pattern };
        }
        SaveConfig(config);
    }

    public bool RemoveCollection(string name)
    {
        var config = LoadConfig();
        if (!config.Collections.Remove(name)) return false;
        SaveConfig(config);
        return true;
    }

    public bool RenameCollection(string oldName, string newName)
    {
        var config = LoadConfig();
        if (!config.Collections.TryGetValue(oldName, out var coll)) return false;
        if (config.Collections.ContainsKey(newName))
            throw new QmdException($"Collection '{newName}' already exists");

        config.Collections[newName] = coll;
        config.Collections.Remove(oldName);
        SaveConfig(config);
        return true;
    }

    /// <summary>
    /// Selectively update collection settings (update command, includeByDefault).
    /// Null values are left unchanged; explicit null for Update clears it.
    /// </summary>
    public bool UpdateCollectionSettings(string name, string? update = null, bool? includeByDefault = null, bool clearUpdate = false)
    {
        var config = LoadConfig();
        if (!config.Collections.TryGetValue(name, out var coll)) return false;

        if (clearUpdate)
            coll.Update = null;
        else if (update != null)
            coll.Update = update;

        if (includeByDefault.HasValue)
            coll.IncludeByDefault = includeByDefault.Value;

        SaveConfig(config);
        return true;
    }

    // =========================================================================
    // Context operations
    // =========================================================================

    public string? GetGlobalContext()
    {
        return LoadConfig().GlobalContext;
    }

    public void SetGlobalContext(string? context)
    {
        var config = LoadConfig();
        config.GlobalContext = context;
        SaveConfig(config);
    }

    public Dictionary<string, string>? GetContexts(string collectionName)
    {
        var config = LoadConfig();
        if (!config.Collections.TryGetValue(collectionName, out var coll)) return null;
        return coll.Context;
    }

    public bool AddContext(string collectionName, string pathPrefix, string contextText)
    {
        var config = LoadConfig();
        if (!config.Collections.TryGetValue(collectionName, out var coll)) return false;
        coll.Context ??= new Dictionary<string, string>();
        coll.Context[pathPrefix] = contextText;
        SaveConfig(config);
        return true;
    }

    public bool RemoveContext(string collectionName, string pathPrefix)
    {
        var config = LoadConfig();
        if (!config.Collections.TryGetValue(collectionName, out var coll)) return false;
        if (coll.Context == null || !coll.Context.Remove(pathPrefix)) return false;
        if (coll.Context.Count == 0) coll.Context = null;
        SaveConfig(config);
        return true;
    }

    public List<(string Collection, string Path, string Context)> ListAllContexts()
    {
        var config = LoadConfig();
        var results = new List<(string, string, string)>();

        if (!string.IsNullOrEmpty(config.GlobalContext))
            results.Add(("*", "/", config.GlobalContext));

        foreach (var (name, coll) in config.Collections)
        {
            if (coll.Context == null) continue;
            foreach (var (path, ctx) in coll.Context)
                results.Add((name, path, ctx));
        }

        return results;
    }

    public string? FindContextForPath(string collectionName, string filePath)
    {
        var config = LoadConfig();
        if (!config.Collections.TryGetValue(collectionName, out var coll))
            return config.GlobalContext;

        if (coll.Context == null || coll.Context.Count == 0)
            return config.GlobalContext;

        // Normalize path
        var normalizedPath = filePath.StartsWith('/') ? filePath : '/' + filePath;

        // Find longest matching prefix
        string? bestMatch = null;
        int bestLen = -1;

        foreach (var prefix in coll.Context.Keys)
        {
            var normalizedPrefix = prefix.StartsWith('/') ? prefix : '/' + prefix;
            if (normalizedPath.StartsWith(normalizedPrefix) && normalizedPrefix.Length > bestLen)
            {
                bestLen = normalizedPrefix.Length;
                bestMatch = prefix;
            }
        }

        if (bestMatch != null)
            return coll.Context[bestMatch];

        return config.GlobalContext;
    }

    // =========================================================================
    // Validation
    // =========================================================================

    public static bool IsValidCollectionName(string name)
    {
        return ValidNameRegex.IsMatch(name);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static string GetDefaultConfigFilePath()
    {
        return Path.Combine(GetConfigDir(), "index.yml");
    }

    private static NamedCollection ToNamed(string name, Collection coll)
    {
        return new NamedCollection
        {
            Name = name,
            Path = coll.Path,
            Pattern = coll.Pattern,
            Ignore = coll.Ignore,
            Context = coll.Context,
            Update = coll.Update,
            IncludeByDefault = coll.IncludeByDefault,
        };
    }
}
