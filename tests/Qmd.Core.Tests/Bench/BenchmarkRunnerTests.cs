using FluentAssertions;
using Qmd.Core.Bench;
using Qmd.Core.Configuration;
using Qmd.Core.Models;
using Qmd.Core.Search;

namespace Qmd.Core.Tests.Bench;

/// <summary>
/// Tests for the benchmark runner with a mock IQmdStore.
/// </summary>
[Trait("Category", "Unit")]
public class BenchmarkRunnerTests : IAsyncDisposable
{
    private readonly IQmdStore _store;

    public BenchmarkRunnerTests()
    {
        _store = new StubQmdStore();
    }

    public ValueTask DisposeAsync() => _store.DisposeAsync();

    [Fact]
    public async Task RunBenchmark_RunsAllBackends_And_ProducesSummary()
    {
        var fixture = new BenchmarkFixture
        {
            Description = "Test fixture",
            Version = 1,
            Collection = "test",
            Queries =
            [
                new BenchmarkQuery
                {
                    Id = "q1",
                    Query = "test query",
                    Type = "exact",
                    Description = "Test query",
                    ExpectedFiles = ["a.md"],
                    ExpectedInTopK = 1,
                },
            ],
        };

        var result = await BenchmarkRunner.RunBenchmarkAsync(_store, fixture, new BenchmarkRunOptions
        {
            Json = true, // suppress stderr output
            Backends = ["bm25"], // only test bm25 since our stub only supports SearchLexAsync
        });

        result.Results.Should().HaveCount(1);
        result.Results[0].Id.Should().Be("q1");
        result.Results[0].Backends.Should().ContainKey("bm25");
        result.Summary.Should().ContainKey("bm25");
    }

    [Fact]
    public async Task RunBenchmark_ComputesSummaryAverages()
    {
        var fixture = new BenchmarkFixture
        {
            Description = "Test fixture",
            Version = 1,
            Queries =
            [
                new BenchmarkQuery
                {
                    Id = "q1",
                    Query = "test",
                    Type = "exact",
                    Description = "First",
                    ExpectedFiles = ["a.md"],
                    ExpectedInTopK = 1,
                },
                new BenchmarkQuery
                {
                    Id = "q2",
                    Query = "other",
                    Type = "exact",
                    Description = "Second",
                    ExpectedFiles = ["z.md"],
                    ExpectedInTopK = 1,
                },
            ],
        };

        var result = await BenchmarkRunner.RunBenchmarkAsync(_store, fixture, new BenchmarkRunOptions
        {
            Json = true,
            Backends = ["bm25"],
        });

        result.Results.Should().HaveCount(2);
        result.Summary.Should().ContainKey("bm25");

        // q1 should find a.md (precision=1), q2 should not find z.md (precision=0)
        // Average should be 0.5
        var summary = result.Summary["bm25"];
        summary.AvgPrecision.Should().BeApproximately(0.5, 0.001);
    }

    [Fact]
    public void ComputeSummary_AveragesCorrectly()
    {
        var results = new List<QueryResult>
        {
            new()
            {
                Id = "q1", Query = "test", Type = "exact",
                Backends = new Dictionary<string, BackendResult>
                {
                    ["bm25"] = new()
                    {
                        PrecisionAtK = 1.0, Recall = 1.0, Mrr = 1.0, F1 = 1.0,
                        LatencyMs = 10, HitsAtK = 1, TotalExpected = 1, TopFiles = ["a.md"],
                    },
                    ["vector"] = new()
                    {
                        PrecisionAtK = 0.5, Recall = 0.5, Mrr = 0.5, F1 = 0.5,
                        LatencyMs = 20, HitsAtK = 1, TotalExpected = 2, TopFiles = ["a.md"],
                    },
                },
            },
            new()
            {
                Id = "q2", Query = "other", Type = "exact",
                Backends = new Dictionary<string, BackendResult>
                {
                    ["bm25"] = new()
                    {
                        PrecisionAtK = 0.0, Recall = 0.0, Mrr = 0.0, F1 = 0.0,
                        LatencyMs = 5, HitsAtK = 0, TotalExpected = 1, TopFiles = [],
                    },
                    ["vector"] = new()
                    {
                        PrecisionAtK = 1.0, Recall = 1.0, Mrr = 1.0, F1 = 1.0,
                        LatencyMs = 30, HitsAtK = 1, TotalExpected = 1, TopFiles = ["b.md"],
                    },
                },
            },
        };

        var summary = BenchmarkRunner.ComputeSummary(results);

        summary.Should().ContainKey("bm25");
        summary.Should().ContainKey("vector");

        summary["bm25"].AvgPrecision.Should().BeApproximately(0.5, 0.001);
        summary["bm25"].AvgRecall.Should().BeApproximately(0.5, 0.001);
        summary["bm25"].AvgMrr.Should().BeApproximately(0.5, 0.001);
        summary["bm25"].AvgF1.Should().BeApproximately(0.5, 0.001);
        summary["bm25"].AvgLatencyMs.Should().BeApproximately(7.5, 0.001);

        summary["vector"].AvgPrecision.Should().BeApproximately(0.75, 0.001);
        summary["vector"].AvgRecall.Should().BeApproximately(0.75, 0.001);
        summary["vector"].AvgMrr.Should().BeApproximately(0.75, 0.001);
        summary["vector"].AvgF1.Should().BeApproximately(0.75, 0.001);
        summary["vector"].AvgLatencyMs.Should().BeApproximately(25.0, 0.001);
    }

    [Fact]
    public async Task RunBenchmark_ThrowsOnEmptyFixture()
    {
        var fixture = new BenchmarkFixture
        {
            Description = "Empty",
            Version = 1,
            Queries = [],
        };

        var act = async () => await BenchmarkRunner.RunBenchmarkAsync(_store, fixture);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*missing*queries*");
    }

    [Fact]
    public void FormatTable_ProducesOutput()
    {
        var benchResult = new BenchmarkResult
        {
            Timestamp = "20260408T120000",
            Fixture = "test",
            Results =
            [
                new QueryResult
                {
                    Id = "q1", Query = "test", Type = "exact",
                    Backends = new Dictionary<string, BackendResult>
                    {
                        ["bm25"] = new()
                        {
                            PrecisionAtK = 1.0, Recall = 1.0, Mrr = 1.0, F1 = 1.0,
                            LatencyMs = 10, HitsAtK = 1, TotalExpected = 1, TopFiles = ["a.md"],
                        },
                    },
                },
            ],
            Summary = new Dictionary<string, BackendSummary>
            {
                ["bm25"] = new()
                {
                    AvgPrecision = 1.0, AvgRecall = 1.0, AvgMrr = 1.0,
                    AvgF1 = 1.0, AvgLatencyMs = 10,
                },
            },
        };

        var table = BenchmarkRunner.FormatTable(benchResult);

        table.Should().Contain("Query");
        table.Should().Contain("Backend");
        table.Should().Contain("bm25");
        table.Should().Contain("Summary");
    }

    [Fact]
    public void FormatJson_ProducesValidJson()
    {
        var benchResult = new BenchmarkResult
        {
            Timestamp = "20260408T120000",
            Fixture = "test",
            Results = [],
            Summary = new Dictionary<string, BackendSummary>
            {
                ["bm25"] = new()
                {
                    AvgPrecision = 0.5, AvgRecall = 0.5, AvgMrr = 0.5,
                    AvgF1 = 0.5, AvgLatencyMs = 10,
                },
            },
        };

        var json = BenchmarkRunner.FormatJson(benchResult);

        json.Should().Contain("\"timestamp\"");
        json.Should().Contain("\"avg_precision\"");
        json.Should().Contain("\"bm25\"");
    }

    /// <summary>
    /// Minimal stub IQmdStore that returns known search results for testing.
    /// Only SearchLexAsync is implemented; other search methods throw to simulate
    /// unavailable backends (which the runner handles gracefully).
    /// </summary>
    private class StubQmdStore : IQmdStore
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<List<SearchResult>> SearchLexAsync(string query, LexSearchOptions? options = null, CancellationToken ct = default)
        {
            // Return a.md and b.md for any query
            var results = new List<SearchResult>
            {
                new() { Filepath = "a.md", DisplayPath = "a.md", Title = "A", Score = 0.9, Hash = "aaa" },
                new() { Filepath = "b.md", DisplayPath = "b.md", Title = "B", Score = 0.5, Hash = "bbb" },
            };
            return Task.FromResult(results);
        }

        public Task<List<SearchResult>> SearchVectorAsync(string query, VectorSearchOptions? options = null, CancellationToken ct = default)
            => throw new NotSupportedException("No embeddings in stub");

        public Task<List<HybridQueryResult>> SearchAsync(SearchOptions options, CancellationToken ct = default)
            => throw new NotSupportedException("No hybrid in stub");

        public Task<List<HybridQueryResult>> SearchStructuredAsync(List<ExpandedQuery> searches, StructuredSearchOptions? options = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<List<ExpandedQuery>> ExpandQueryAsync(string query, ExpandQuerySdkOptions? options = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<FindDocumentResult> GetAsync(string pathOrDocId, GetOptions? options = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string?> GetDocumentBodyAsync(string pathOrDocId, BodyOptions? options = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<(List<MultiGetResult> Docs, List<string> Errors)> MultiGetAsync(string pattern, MultiGetOptions? options = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<List<ListFileEntry>> ListFilesAsync(string collection, string? pathPrefix = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task AddCollectionAsync(string name, string path, string pattern = "**/*.md", List<string>? ignore = null)
            => throw new NotSupportedException();

        public Task<bool> RemoveCollectionAsync(string name)
            => throw new NotSupportedException();

        public Task<bool> RenameCollectionAsync(string oldName, string newName)
            => throw new NotSupportedException();

        public Task<List<NamedCollection>> ListCollectionsAsync()
            => Task.FromResult(new List<NamedCollection>());

        public Task<List<string>> GetDefaultCollectionNamesAsync()
            => Task.FromResult(new List<string>());

        public Task<bool> UpdateCollectionSettingsAsync(string name, string? update = null, bool? includeByDefault = null, bool clearUpdate = false)
            => throw new NotSupportedException();

        public Task<bool> AddContextAsync(string collection, string pathPrefix, string text)
            => throw new NotSupportedException();

        public Task<bool> RemoveContextAsync(string collection, string pathPrefix)
            => throw new NotSupportedException();

        public Task SetGlobalContextAsync(string? context)
            => throw new NotSupportedException();

        public Task<string?> GetGlobalContextAsync()
            => throw new NotSupportedException();

        public Task<List<(string Collection, string Path, string Context)>> ListContextsAsync()
            => throw new NotSupportedException();

        public Task<ReindexResult> UpdateAsync(UpdateOptions? options = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<EmbedResult> EmbedAsync(EmbedPipelineOptions? options = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IndexStatus> GetStatusAsync()
            => throw new NotSupportedException();

        public Task<IndexHealthInfo> GetIndexHealthAsync()
            => throw new NotSupportedException();

        public Task<List<HybridQueryResult>> HybridQueryAsync(string query, HybridQueryOptions? options = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string?> GetContextForFileAsync(string filepath)
            => throw new NotSupportedException();

        public Task<List<string>> FindSimilarFilesAsync(string query, int maxDistance = 3, int limit = 5, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<List<string>> GetActiveDocumentPathsAsync(string collection, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<EmbeddingProfile> ProfileEmbeddingsAsync(EmbeddingProfileOptions? options = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public SearchConfig SearchConfig { get; set; } = new();

        public Task SaveSearchConfigAsync(SearchConfig config)
            => throw new NotSupportedException();

        public Task ResetSearchConfigAsync()
            => throw new NotSupportedException();

        public Task<CleanupResult> CleanupAsync(CleanupOptions? options = null, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
