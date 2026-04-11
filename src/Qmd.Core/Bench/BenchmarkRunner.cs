using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Qmd.Core.Bench;

/// <summary>
/// Options for running a benchmark.
/// </summary>
public class BenchmarkRunOptions
{
    /// <summary>Output results as JSON instead of table.</summary>
    public bool Json { get; init; }

    /// <summary>Override the fixture's collection filter.</summary>
    public string? Collection { get; init; }

    /// <summary>Filter to specific backends (bm25, vector, hybrid, full).</summary>
    public List<string>? Backends { get; init; }
}

/// <summary>
/// Runs benchmark queries across multiple search backends and measures
/// precision@k, recall, MRR, F1, and latency.
/// <para>
/// Each <see cref="BenchmarkFixture"/> contains queries with ground-truth expected files.
/// The runner executes every query against four backends (bm25, vector, hybrid, full)
/// and scores the results using <see cref="BenchmarkScorer"/>.
/// </para>
/// </summary>
public static class BenchmarkRunner
{
    private record Backend(string Name, Func<IQmdStore, string, int, List<string>?, Task<List<string>>> Run);

    private static readonly List<Backend> AllBackends =
    [
        new("bm25", async (store, query, limit, collections) =>
        {
            var results = await store.SearchLexAsync(query, new LexSearchOptions
            {
                Limit = limit,
                Collections = collections,
            });
            return results.Select(r => r.Filepath).ToList();
        }),
        new("vector", async (store, query, limit, collections) =>
        {
            var results = await store.SearchVectorAsync(query, new VectorSearchOptions
            {
                Limit = limit,
                Collections = collections,
            });
            return results.Select(r => r.Filepath).ToList();
        }),
        new("hybrid", async (store, query, limit, collections) =>
        {
            var results = await store.SearchAsync(new SearchOptions
            {
                Query = query,
                Limit = limit,
                Collections = collections,
                SkipRerank = true,
            });
            return results.Select(r => r.File).ToList();
        }),
        new("full", async (store, query, limit, collections) =>
        {
            var results = await store.SearchAsync(new SearchOptions
            {
                Query = query,
                Limit = limit,
                Collections = collections,
                SkipRerank = false,
            });
            return results.Select(r => r.File).ToList();
        }),
    ];

    /// <summary>
    /// Run a benchmark fixture against all (or filtered) backends.
    /// Each query is executed against every active backend; results are scored
    /// against the fixture's expected files and aggregated into a summary.
    /// </summary>
    /// <param name="store">The QMD store to search against.</param>
    /// <param name="fixture">Benchmark fixture with queries and expected results.</param>
    /// <param name="options">Backend filters, collection override, and output format.</param>
    /// <returns>Per-query scores and per-backend summary averages.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the fixture has no queries.</exception>
    public static async Task<BenchmarkResult> RunBenchmarkAsync(
        IQmdStore store,
        BenchmarkFixture fixture,
        BenchmarkRunOptions? options = null)
    {
        options ??= new BenchmarkRunOptions();

        if (fixture.Queries is not { Count: > 0 })
            throw new InvalidOperationException("Invalid fixture: missing 'queries' array");

        // Filter backends if requested
        var activeBackends = options.Backends is { Count: > 0 }
            ? AllBackends.Where(b => options.Backends.Contains(b.Name)).ToList()
            : AllBackends;

        var collection = options.Collection ?? fixture.Collection;
        var collections = collection != null ? new List<string> { collection } : null;

        // Run queries
        var results = new List<QueryResult>();
        foreach (var query in fixture.Queries)
        {
            var backends = new Dictionary<string, BackendResult>();

            foreach (var backend in activeBackends)
            {
                if (!options.Json)
                    Console.Error.Write($"  {query.Id} / {backend.Name}...");

                var backendResult = await RunQueryAsync(store, backend, query, collections);
                backends[backend.Name] = backendResult;

                if (!options.Json)
                    Console.Error.WriteLine($" {Math.Round(backendResult.LatencyMs)}ms");
            }

            results.Add(new QueryResult
            {
                Id = query.Id,
                Query = query.Query,
                Type = query.Type,
                Backends = backends,
            });
        }

        var summary = ComputeSummary(results);
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HHmm");

        return new BenchmarkResult
        {
            Timestamp = timestamp,
            Fixture = fixture.Description,
            Results = results,
            Summary = summary,
        };
    }

    /// <summary>
    /// Run a single query against one backend, returning scored results.
    /// Returns zero scores if the backend throws (e.g. no embeddings for vector search).
    /// </summary>
    private static async Task<BackendResult> RunQueryAsync(
        IQmdStore store,
        Backend backend,
        BenchmarkQuery query,
        List<string>? collections)
    {
        var limit = Math.Max(query.ExpectedInTopK, 10);
        var sw = Stopwatch.StartNew();

        List<string> resultFiles;
        try
        {
            resultFiles = await backend.Run(store, query.Query, limit, collections);
        }
        catch
        {
            // Backend may not be available (e.g., no embeddings for vector search)
            return new BackendResult
            {
                PrecisionAtK = 0,
                Recall = 0,
                Mrr = 0,
                F1 = 0,
                HitsAtK = 0,
                TotalExpected = query.ExpectedFiles.Count,
                LatencyMs = sw.Elapsed.TotalMilliseconds,
                TopFiles = [],
            };
        }

        sw.Stop();
        var scores = BenchmarkScorer.ScoreResults(resultFiles, query.ExpectedFiles, query.ExpectedInTopK);

        return new BackendResult
        {
            PrecisionAtK = scores.PrecisionAtK,
            Recall = scores.Recall,
            Mrr = scores.Mrr,
            F1 = scores.F1,
            HitsAtK = scores.HitsAtK,
            TotalExpected = query.ExpectedFiles.Count,
            LatencyMs = sw.Elapsed.TotalMilliseconds,
            TopFiles = resultFiles.Take(10).ToList(),
        };
    }

    /// <summary>
    /// Compute average metrics per backend across all query results.
    /// </summary>
    /// <param name="results">Per-query results from <see cref="RunBenchmarkAsync"/>.</param>
    /// <returns>One <see cref="BackendSummary"/> per backend with averaged metrics.</returns>
    public static Dictionary<string, BackendSummary> ComputeSummary(List<QueryResult> results)
    {
        var summary = new Dictionary<string, BackendSummary>();

        // Collect all backend names
        var backendNames = new HashSet<string>();
        foreach (var r in results)
        foreach (var name in r.Backends.Keys)
            backendNames.Add(name);

        foreach (var name in backendNames)
        {
            double totalP = 0, totalR = 0, totalMrr = 0, totalF1 = 0, totalLat = 0;
            int count = 0;

            foreach (var r in results)
            {
                if (!r.Backends.TryGetValue(name, out var br))
                    continue;
                totalP += br.PrecisionAtK;
                totalR += br.Recall;
                totalMrr += br.Mrr;
                totalF1 += br.F1;
                totalLat += br.LatencyMs;
                count++;
            }

            if (count > 0)
            {
                summary[name] = new BackendSummary
                {
                    AvgPrecision = totalP / count,
                    AvgRecall = totalR / count,
                    AvgMrr = totalMrr / count,
                    AvgF1 = totalF1 / count,
                    AvgLatencyMs = totalLat / count,
                };
            }
        }

        return summary;
    }

    /// <summary>
    /// Format benchmark results as a human-readable table with per-query rows and a summary footer.
    /// </summary>
    /// <param name="benchResult">The benchmark results to format.</param>
    /// <returns>A formatted multi-line string suitable for console output.</returns>
    public static string FormatTable(BenchmarkResult benchResult)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"{Pad("Query", 25)} {Pad("Backend", 8)} {Pad("P@k", 6)} {Pad("Recall", 7)} {Pad("MRR", 6)} {Pad("F1", 6)} {Pad("ms", 8)}");
        sb.AppendLine(new string('-', 70));

        foreach (var r in benchResult.Results)
        {
            foreach (var (backend, br) in r.Backends)
            {
                sb.AppendLine(
                    $"{Pad(r.Id, 25)} {Pad(backend, 8)} {Num(br.PrecisionAtK)} {Num(br.Recall)}  {Num(br.Mrr)} {Num(br.F1)} {Math.Round(br.LatencyMs).ToString().PadLeft(7)}ms");
            }
            sb.AppendLine();
        }

        sb.AppendLine("Summary:");
        sb.AppendLine(new string('-', 70));

        foreach (var (name, s) in benchResult.Summary)
        {
            sb.AppendLine(
                $"  {Pad(name, 8)} P@k={Num3(s.AvgPrecision)} Recall={Num3(s.AvgRecall)} MRR={Num3(s.AvgMrr)} F1={Num3(s.AvgF1)} Avg={Math.Round(s.AvgLatencyMs)}ms");
        }

        return sb.ToString();

        static string Pad(string s, int n)
        {
            if (s.Length > n) s = s[..n];
            return s.PadRight(n);
        }

        static string Num(double n) => n.ToString("F2").PadLeft(5);
        static string Num3(double n) => n.ToString("F3").PadLeft(6);
    }

    /// <summary>
    /// Format benchmark results as indented JSON.
    /// </summary>
    /// <param name="benchResult">The benchmark results to serialize.</param>
    /// <returns>A JSON string.</returns>
    public static string FormatJson(BenchmarkResult benchResult)
    {
        return JsonSerializer.Serialize(benchResult, new JsonSerializerOptions
        {
            WriteIndented = true,
        });
    }
}
