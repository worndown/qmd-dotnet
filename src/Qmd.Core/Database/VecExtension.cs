namespace Qmd.Core.Database;

/// <summary>
/// Manages sqlite-vec extension loading and vector table operations.
/// </summary>
internal static class VecExtension
{
    private static bool? _isAvailable;

    public static bool IsAvailable => _isAvailable == true;

    /// <summary>
    /// Attempt to load sqlite-vec and verify it. Sets IsAvailable flag.
    /// Gracefully fails — vector search will be unavailable but FTS works.
    /// </summary>
    public static void TryLoad(IQmdDatabase db)
    {
        try
        {
            Load(db);
            Verify(db);
            _isAvailable = true;
        }
        catch
        {
            // sqlite-vec not available — vector search disabled, FTS still works
            _isAvailable = false;
        }
    }

    /// <summary>
    /// Load the sqlite-vec extension. Throws on failure.
    /// </summary>
    public static void Load(IQmdDatabase db)
    {
        // Find vec0.dll relative to the application
        var assemblyDir = AppContext.BaseDirectory;
        var vecPath = Path.Combine(assemblyDir, "vec0");

        // Also check runtimes/win-x64/native/
        var runtimePath = Path.Combine(assemblyDir, "runtimes", "win-x64", "native", "vec0");

        try
        {
            db.LoadExtension(vecPath);
        }
        catch
        {
            try
            {
                db.LoadExtension(runtimePath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to load sqlite-vec extension. Ensure vec0.dll is present alongside the application. {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Verify that sqlite-vec is loaded by calling vec_version().
    /// </summary>
    public static void Verify(IQmdDatabase db)
    {
        try
        {
            var row = db.Prepare("SELECT vec_version() AS version").GetDynamic();
            if (row == null || row["version"] is not string version || string.IsNullOrEmpty(version))
                throw new InvalidOperationException("vec_version() returned no version");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"sqlite-vec probe failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensure the vectors_vec virtual table exists with the correct dimensions.
    /// Validates dimension consistency with existing table.
    /// </summary>
    public static void EnsureVecTable(IQmdDatabase db, int dimensions)
    {
        if (!IsAvailable)
            throw new InvalidOperationException("sqlite-vec extension is not available. Cannot create vector table.");

        // Check if table already exists and validate dimensions + structure
        var existing = db.Prepare(
            "SELECT sql FROM sqlite_master WHERE type='table' AND name='vectors_vec'").GetDynamic();

        if (existing != null)
        {
            var sql = existing["sql"]?.ToString() ?? "";
            var match = System.Text.RegularExpressions.Regex.Match(sql, @"float\[(\d+)\]");
            var hasHashSeq = sql.Contains("hash_seq", StringComparison.OrdinalIgnoreCase);
            var hasCosine = sql.Contains("distance_metric=cosine", StringComparison.OrdinalIgnoreCase);

            if (match.Success && int.TryParse(match.Groups[1].Value, out int existingDims))
            {
                if (existingDims == dimensions && hasHashSeq && hasCosine)
                    return; // Table exists with correct schema

                if (existingDims != dimensions)
                {
                    throw new InvalidOperationException(
                        $"Embedding dimension mismatch: existing vectors are {existingDims}d " +
                        $"but the current model produces {dimensions}d. " +
                        "Run 'qmd embed -f' to re-embed with the new model.");
                }
            }

            // Schema mismatch (missing hash_seq PK or cosine metric) — rebuild
            db.Exec("DROP TABLE IF EXISTS vectors_vec");
        }

        db.Exec($@"
            CREATE VIRTUAL TABLE vectors_vec USING vec0(
                hash_seq TEXT PRIMARY KEY,
                embedding float[{dimensions}] distance_metric=cosine
            )
        ");
    }

    /// <summary>Reset availability flag — for testing only.</summary>
    public static void ResetForTesting() => _isAvailable = null;
}
