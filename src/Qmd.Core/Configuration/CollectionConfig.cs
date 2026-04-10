using YamlDotNet.Serialization;

namespace Qmd.Core.Configuration;

public class CollectionConfig
{
    [YamlMember(Alias = "global_context")]
    public string? GlobalContext { get; set; }

    [YamlMember(Alias = "editor_uri")]
    public string? EditorUri { get; set; }

    [YamlMember(Alias = "editor_uri_template")]
    public string? EditorUriTemplate { get; set; }

    [YamlMember(Alias = "collections")]
    public Dictionary<string, Collection> Collections { get; set; } = new();

    [YamlMember(Alias = "models")]
    public ModelsConfig? Models { get; set; }
}

public class Collection
{
    [YamlMember(Alias = "path")]
    public string Path { get; set; } = "";

    [YamlMember(Alias = "pattern")]
    public string Pattern { get; set; } = "**/*.md";

    [YamlMember(Alias = "ignore")]
    public List<string>? Ignore { get; set; }

    [YamlMember(Alias = "context")]
    public Dictionary<string, string>? Context { get; set; }

    [YamlMember(Alias = "update")]
    public string? Update { get; set; }

    [YamlMember(Alias = "include_by_default")]
    public bool? IncludeByDefault { get; set; }
}

public class NamedCollection : Collection
{
    [YamlIgnore]
    public string Name { get; set; } = "";
}

public class ModelsConfig
{
    [YamlMember(Alias = "embed")]
    public string? Embed { get; set; }

    [YamlMember(Alias = "rerank")]
    public string? Rerank { get; set; }

    [YamlMember(Alias = "generate")]
    public string? Generate { get; set; }
}
