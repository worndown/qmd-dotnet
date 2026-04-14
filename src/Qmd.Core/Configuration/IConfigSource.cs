namespace Qmd.Core.Configuration;

/// <summary>
/// Abstraction over config loading/saving. Supports file-based and in-memory modes.
/// </summary>
public interface IConfigSource
{
    CollectionConfig Load();
    void Save(CollectionConfig config);
    string DisplayPath { get; }
    bool Exists { get; }
}
