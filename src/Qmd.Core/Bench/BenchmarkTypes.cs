using System.Text.Json.Serialization;

namespace Qmd.Core.Bench;

/// <summary>
/// Types for the QMD benchmark harness.
/// A benchmark fixture defines queries with expected results.
/// The harness runs each query through multiple search backends
/// and measures precision, recall, MRR, and latency.
/// </summary>
public class BenchmarkQuery
{
    /// <summary>Unique identifier for the query.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>The search query text.</summary>
    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    /// <summary>Query difficulty/type for grouping results.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    /// <summary>Human-readable description of what this tests.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>File paths (relative to collection) that should appear in results.</summary>
    [JsonPropertyName("expected_files")]
    public List<string> ExpectedFiles { get; set; } = [];

    /// <summary>How many of expected_files should appear in top-k results.</summary>
    [JsonPropertyName("expected_in_top_k")]
    public int ExpectedInTopK { get; set; }
}

public class BenchmarkFixture
{
    /// <summary>Description of the benchmark.</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>Fixture format version.</summary>
    [JsonPropertyName("version")]
    public int Version { get; set; }

    /// <summary>Optional collection to search within.</summary>
    [JsonPropertyName("collection")]
    public string? Collection { get; set; }

    /// <summary>The test queries.</summary>
    [JsonPropertyName("queries")]
    public List<BenchmarkQuery> Queries { get; set; } = [];
}

public class BackendResult
{
    /// <summary>Fraction of top-k results that are relevant.</summary>
    [JsonPropertyName("precision_at_k")]
    public double PrecisionAtK { get; set; }

    /// <summary>Fraction of expected files found anywhere in results.</summary>
    [JsonPropertyName("recall")]
    public double Recall { get; set; }

    /// <summary>Reciprocal rank of first relevant result (1/rank, 0 if not found).</summary>
    [JsonPropertyName("mrr")]
    public double Mrr { get; set; }

    /// <summary>Harmonic mean of precision_at_k and recall.</summary>
    [JsonPropertyName("f1")]
    public double F1 { get; set; }

    /// <summary>Number of expected files found in top-k.</summary>
    [JsonPropertyName("hits_at_k")]
    public int HitsAtK { get; set; }

    /// <summary>Total expected files.</summary>
    [JsonPropertyName("total_expected")]
    public int TotalExpected { get; set; }

    /// <summary>Wall-clock latency in milliseconds.</summary>
    [JsonPropertyName("latency_ms")]
    public double LatencyMs { get; set; }

    /// <summary>Top result file paths (for inspection).</summary>
    [JsonPropertyName("top_files")]
    public List<string> TopFiles { get; set; } = [];
}

public class QueryResult
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("query")]
    public string Query { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("backends")]
    public Dictionary<string, BackendResult> Backends { get; set; } = new();
}

public class BackendSummary
{
    [JsonPropertyName("avg_precision")]
    public double AvgPrecision { get; set; }

    [JsonPropertyName("avg_recall")]
    public double AvgRecall { get; set; }

    [JsonPropertyName("avg_mrr")]
    public double AvgMrr { get; set; }

    [JsonPropertyName("avg_f1")]
    public double AvgF1 { get; set; }

    [JsonPropertyName("avg_latency_ms")]
    public double AvgLatencyMs { get; set; }
}

public class BenchmarkResult
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("fixture")]
    public string Fixture { get; set; } = "";

    [JsonPropertyName("results")]
    public List<QueryResult> Results { get; set; } = [];

    [JsonPropertyName("summary")]
    public Dictionary<string, BackendSummary> Summary { get; set; } = new();
}
