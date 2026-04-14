using Qmd.Core.Configuration;
using Qmd.Core.Database;
using Qmd.Core.Documents;
using Qmd.Core.Embedding;
using Qmd.Core.Indexing;
using Qmd.Core.Llm;
using Qmd.Core.Search;
using Qmd.Core.Store;

namespace Qmd.Core;

/// <summary>
/// Options for creating a <see cref="IQmdStore"/> via <see cref="QmdStoreFactory"/>.
/// </summary>
public class StoreOptions
{
    /// <summary>
    /// Absolute path to the SQLite database file.
    /// The file is created automatically if it does not exist.
    /// </summary>
    public required string DbPath { get; init; }

    /// <summary>
    /// Path to a YAML configuration file. Mutually exclusive with <see cref="Config"/>.
    /// When omitted and <see cref="Config"/> is also <c>null</c>, the default config
    /// location is used (<c>~/.config/qmd/config.yaml</c>).
    /// </summary>
    public string? ConfigPath { get; init; }

    /// <summary>
    /// Inline collection configuration. Mutually exclusive with <see cref="ConfigPath"/>.
    /// Useful for tests and embedded scenarios.
    /// </summary>
    public CollectionConfig? Config { get; init; }

    /// <summary>
    /// Optional LLM service for embedding, reranking, and query expansion.
    /// When <c>null</c>, operations requiring an LLM will throw on first use.
    /// </summary>
    public ILlmService? LlmService { get; init; }
}

/// <summary>
/// Factory for creating <see cref="IQmdStore"/> instances.
/// This is the primary entry point for SDK consumers.
/// </summary>
public static class QmdStoreFactory
{
    /// <summary>
    /// Create a store backed by a SQLite database on disk.
    /// </summary>
    /// <param name="options">Database path, config source, and optional LLM service.</param>
    /// <returns>A fully initialized <see cref="IQmdStore"/> ready for search and retrieval.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <see cref="StoreOptions.DbPath"/> is empty, or both
    /// <see cref="StoreOptions.ConfigPath"/> and <see cref="StoreOptions.Config"/> are provided.
    /// </exception>
    public static Task<IQmdStore> CreateAsync(StoreOptions options)
    {
        if (string.IsNullOrEmpty(options.DbPath))
            throw new ArgumentException("dbPath is required");
        if (options.Config != null && options.ConfigPath != null)
            throw new ArgumentException("Provide either ConfigPath or Config, not both.");

        ConfigManager configManager;
        if (options.Config != null)
        {
            configManager = new ConfigManager(new InlineConfigSource(options.Config));
        }
        else if (options.ConfigPath != null)
        {
            configManager = new ConfigManager(new FileConfigSource(options.ConfigPath));
        }
        else
        {
            configManager = new ConfigManager();
        }

        var store = new QmdStore(options.DbPath, configManager, options.LlmService);
        return Task.FromResult<IQmdStore>(store);
    }

    /// <summary>
    /// Create an in-memory store for testing. The database lives only for the
    /// lifetime of the returned <see cref="IQmdStore"/> and is not persisted.
    /// </summary>
    /// <param name="config">Optional inline collection configuration.</param>
    /// <param name="llmService">Optional LLM service for search features.</param>
    public static Task<IQmdStore> CreateInMemoryAsync(CollectionConfig? config = null, ILlmService? llmService = null)
    {
        var db = new SqliteDatabase(":memory:");
        var configManager = new ConfigManager(new InlineConfigSource(config ?? new CollectionConfig()));
        var store = new QmdStore(db, configManager, llmService);
        return Task.FromResult<IQmdStore>(store);
    }
}
