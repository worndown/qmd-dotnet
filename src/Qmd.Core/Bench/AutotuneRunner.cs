using Qmd.Core.Models;
using Qmd.Core.Search;

namespace Qmd.Core.Bench;

public class AutotuneResult
{
    public required SearchConfig Config { get; init; }
    public required EmbeddingProfile Profile { get; init; }
    public BenchmarkResult? BaselineBench { get; init; }
    public BenchmarkResult? TunedBench { get; init; }
    public bool ImprovedOverBaseline { get; init; } = true;
}

public static class AutotuneRunner
{
    public static async Task<AutotuneResult> ProfileBasedAutotuneAsync(
        IQmdStore store,
        EmbeddingProfileOptions? options = null,
        CancellationToken ct = default)
    {
        var profile = await store.ProfileEmbeddingsAsync(options, ct);

        var config = new SearchConfig
        {
            VecOnlyGateThreshold = profile.P25,
        };

        return new AutotuneResult
        {
            Config = config,
            Profile = profile,
        };
    }

    public static async Task<AutotuneResult> BenchBasedAutotuneAsync(
        IQmdStore store,
        BenchmarkFixture fixture,
        string? collection,
        EmbeddingProfileOptions? profileOptions = null,
        CancellationToken ct = default)
    {
        // Step 1: Run profile-based to fix VecOnlyGateThreshold
        var profileResult = await ProfileBasedAutotuneAsync(store, profileOptions, ct);
        var vecGate = profileResult.Profile.P25;

        var originalConfig = store.SearchConfig;
        try
        {
            // Step 2: Run baseline bench with current config
            var baselineResult = await BenchmarkRunner.RunBenchmarkAsync(store, fixture,
                new BenchmarkRunOptions
                {
                    Backends = ["hybrid"],
                    Json = true,
                    Collection = collection,
                }, ct);

            var baselineF1 = baselineResult.Summary.TryGetValue("hybrid", out var bs) ? bs.AvgF1 : 0.0;

            // Step 3: Grid search
            double[] ftsSignalValues = [0.2, 0.3, 0.4];
            double[] gapRatioValues = [0.3, 0.5, 0.7];

            SearchConfig bestConfig = profileResult.Config;
            double bestF1 = 0.0;
            BenchmarkResult? bestBenchResult = null;

            foreach (var ftsMin in ftsSignalValues)
            {
                foreach (var gap in gapRatioValues)
                {
                    ct.ThrowIfCancellationRequested();

                    var candidate = new SearchConfig
                    {
                        VecOnlyGateThreshold = vecGate,
                        FtsMinSignal = ftsMin,
                        ConfidenceGapRatio = gap,
                    };

                    store.SearchConfig = candidate;

                    var result = await BenchmarkRunner.RunBenchmarkAsync(store, fixture,
                        new BenchmarkRunOptions
                        {
                            Backends = ["hybrid"],
                            Json = true,
                            Collection = collection,
                        }, ct);

                    var f1 = result.Summary.TryGetValue("hybrid", out var s) ? s.AvgF1 : 0.0;

                    if (f1 > bestF1)
                    {
                        bestF1 = f1;
                        bestConfig = candidate;
                        bestBenchResult = result;
                    }
                }
            }

            // Step 4: Regression guard
            var improved = bestF1 > baselineF1;
            return new AutotuneResult
            {
                Config = improved ? bestConfig : profileResult.Config,
                Profile = profileResult.Profile,
                BaselineBench = baselineResult,
                TunedBench = improved ? bestBenchResult : null,
                ImprovedOverBaseline = improved,
            };
        }
        finally
        {
            store.SearchConfig = originalConfig;
        }
    }
}
