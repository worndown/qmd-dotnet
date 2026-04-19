using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Qmd.Core.Configuration;

/// <summary>
/// File-based config source using YAML.
/// </summary>
public class FileConfigSource : IConfigSource
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    // Build a value serializer so we can pair it with custom EmitterSettings that disable line wrapping.
    private static readonly IValueSerializer ValueSerializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .WithIndentedSequences()
        .BuildValueSerializer();

    private static readonly ISerializer Serializer = YamlDotNet.Serialization.Serializer.FromValueSerializer(
        ValueSerializer,
        new EmitterSettings(
            bestIndent: 2,
            bestWidth: int.MaxValue,
            isCanonical: false,
            maxSimpleKeyLength: 1024,
            skipAnchorName: false,
            indentSequences: true,
            newLine: "\n",
            useUtf16SurrogatePairs: false));

    public string FilePath { get; }
    public string DisplayPath => this.FilePath;
    public bool Exists => File.Exists(this.FilePath);

    public FileConfigSource(string filePath)
    {
        this.FilePath = filePath;
    }

    public CollectionConfig Load()
    {
        if (!File.Exists(this.FilePath))
            return new CollectionConfig();

        var yaml = File.ReadAllText(this.FilePath);
        if (string.IsNullOrWhiteSpace(yaml))
            return new CollectionConfig();

        var config = Deserializer.Deserialize<CollectionConfig>(yaml);
        config ??= new CollectionConfig();
        config.Collections ??= new Dictionary<string, Collection>();
        return config;
    }

    public void Save(CollectionConfig config)
    {
        var dir = Path.GetDirectoryName(this.FilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var yaml = Serializer.Serialize(config);
        File.WriteAllText(this.FilePath, yaml);
    }
}
