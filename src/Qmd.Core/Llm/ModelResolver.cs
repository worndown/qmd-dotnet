namespace Qmd.Core.Llm;

/// <summary>
/// Resolves HuggingFace model URIs to local file paths.
/// Downloads models on first use, caches with etag freshness.
/// </summary>
public class ModelResolver
{
    private readonly HttpClient _httpClient;
    private readonly string _cacheDir;

    public record HfRef(string Repo, string File);

    public ModelResolver(HttpClient? httpClient = null, string? cacheDir = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _cacheDir = cacheDir ?? LlmConstants.GetModelCacheDir();
    }

    /// <summary>
    /// Parse a HuggingFace URI like "hf:user/repo/file.gguf" into repo and file components.
    /// </summary>
    public static HfRef? ParseHfUri(string modelUri)
    {
        if (!modelUri.StartsWith("hf:")) return null;
        var parts = modelUri[3..].Split('/');
        if (parts.Length < 3) return null;
        var repo = $"{parts[0]}/{parts[1]}";
        var file = string.Join('/', parts[2..]);
        return new HfRef(repo, file);
    }

    /// <summary>
    /// Resolve a model URI to a local file path. Downloads if not cached.
    /// </summary>
    /// <param name="modelUri">HuggingFace URI (hf:user/repo/file) or local path.</param>
    /// <param name="refresh">Force re-download even if cached.</param>
    /// <param name="onProgress">Optional callback for progress messages (e.g. "Downloading model...").</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<string> ResolveModelFileAsync(string modelUri, bool refresh = false, Action<string>? onProgress = null, CancellationToken ct = default)
    {
        var hfRef = ParseHfUri(modelUri);
        if (hfRef == null)
        {
            // Not an HF URI — treat as local path
            if (File.Exists(modelUri)) return modelUri;
            throw new FileNotFoundException($"Model file not found: {modelUri}");
        }

        Directory.CreateDirectory(_cacheDir);

        var localPath = Path.Combine(_cacheDir, hfRef.File);
        var etagPath = localPath + ".etag";

        // Check cache (skip if refresh requested)
        if (File.Exists(localPath) && !refresh)
        {
            // Check etag freshness if etag file exists
            if (File.Exists(etagPath))
            {
                try
                {
                    var storedEtag = await File.ReadAllTextAsync(etagPath, ct);
                    var headReq = new HttpRequestMessage(HttpMethod.Head,
                        $"https://huggingface.co/{hfRef.Repo}/resolve/main/{hfRef.File}");
                    var headResp = await _httpClient.SendAsync(headReq, ct);
                    if (!headResp.IsSuccessStatusCode)
                        return localPath; // Server error — use cached
                    if (headResp.Headers.ETag?.Tag == storedEtag)
                        return localPath; // Still fresh
                    // Stale — fall through to re-download
                }
                catch
                {
                    return localPath; // Network error — use cached
                }
            }
            else
            {
                return localPath;
            }
        }

        // Download from HuggingFace
        var url = $"https://huggingface.co/{hfRef.Repo}/resolve/main/{hfRef.File}";
        onProgress?.Invoke($"Downloading model: {modelUri}...");

        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        // Save etag if provided
        if (response.Headers.ETag != null)
        {
            await File.WriteAllTextAsync(etagPath, response.Headers.ETag.Tag, ct);
        }

        // Stream to file
        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
        await using var fileStream = File.Create(localPath);
        await response.Content.CopyToAsync(fileStream, ct);

        onProgress?.Invoke($"Model saved to: {localPath}");
        return localPath;
    }
}
