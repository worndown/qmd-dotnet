namespace Qmd.Core.Configuration;

/// <summary>
/// In-memory config source for SDK mode. No file I/O.
/// </summary>
public class InlineConfigSource : IConfigSource
{
    private CollectionConfig config;

    public string DisplayPath => "<inline>";
    public bool Exists => true;

    public InlineConfigSource(CollectionConfig? config = null)
    {
        this.config = config ?? new CollectionConfig();
        this.config.Collections ??= new Dictionary<string, Collection>();
    }

    public CollectionConfig Load() => this.config;

    public void Save(CollectionConfig config)
    {
        this.config = config;
        this.config.Collections ??= new Dictionary<string, Collection>();
    }
}
