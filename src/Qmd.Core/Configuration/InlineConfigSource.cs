namespace Qmd.Core.Configuration;

/// <summary>
/// In-memory config source for SDK mode. No file I/O.
/// </summary>
public class InlineConfigSource : IConfigSource
{
    private CollectionConfig _config;

    public string DisplayPath => "<inline>";
    public bool Exists => true;

    public InlineConfigSource(CollectionConfig? config = null)
    {
        _config = config ?? new CollectionConfig();
        _config.Collections ??= new Dictionary<string, Collection>();
    }

    public CollectionConfig Load() => _config;

    public void Save(CollectionConfig config)
    {
        _config = config;
        _config.Collections ??= new Dictionary<string, Collection>();
    }
}
