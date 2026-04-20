using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Qmd.Core.Content;
using Qmd.Core.Models;
using Qmd.Core.Snippets;

namespace Qmd.Core.Mcp;

/// <summary>
/// Helper to set up the MCP server with stdio or HTTP transport.
/// </summary>
public static class McpServerSetup
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string GetVersion() =>
        typeof(McpServerSetup).Assembly.GetName().Version?.ToString(3) ?? "2.1.0";

    /// <summary>
    /// Create and run an MCP server with stdio transport.
    /// </summary>
    public static async Task RunStdioAsync(IQmdStore store, CancellationToken ct = default)
    {
        var instructions = await InstructionsBuilder.BuildAsync(store);

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton(store);
        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new() { Name = "qmd", Version = GetVersion() };
                options.ServerInstructions = instructions;
            })
            .WithStdioServerTransport()
            .WithTools<QmdTools>()
            .WithResources<QmdResources>();

        var app = builder.Build();
        await app.RunAsync(ct);
    }

    /// <summary>
    /// Create and run an MCP server with HTTP transport (Streamable HTTP).
    /// </summary>
    public static async Task RunHttpAsync(IQmdStore store, int port = 8181, CancellationToken ct = default)
    {
        // Redirect console output to log file when running as daemon
        var logFile = Environment.GetEnvironmentVariable("QMD_LOG_FILE");
        if (!string.IsNullOrEmpty(logFile))
        {
            var writer = new StreamWriter(logFile, append: false) { AutoFlush = true };
            Console.SetOut(writer);
            Console.SetError(writer);
        }

        var instructions = await InstructionsBuilder.BuildAsync(store);

        var uptime = Stopwatch.StartNew();

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(store);
        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new() { Name = "qmd", Version = GetVersion() };
                options.ServerInstructions = instructions;
            })
            .WithHttpTransport()
            .WithTools<QmdTools>()
            .WithResources<QmdResources>();

        var app = builder.Build();
        app.MapMcp();

        // GET /health — server liveness check
        app.MapGet("/health", () => Results.Json(
            new { status = "ok", uptime = (int)uptime.Elapsed.TotalSeconds }, JsonOpts));

        // POST /query and POST /search — REST shortcut for structured search
        async Task<IResult> HandleSearchEndpoint(HttpRequest request, IQmdStore s)
        {
            SearchRequest? body;
            try
            {
                body = await request.ReadFromJsonAsync<SearchRequest>(JsonOpts);
            }
            catch
            {
                return Results.Json(new { error = "Invalid JSON body" }, JsonOpts, statusCode: 400);
            }

            if (body?.Searches is not { Count: > 0 })
                return Results.Json(new { error = "Missing required field: searches (array)" }, JsonOpts, statusCode: 400);

            var primaryQuery = (body.Searches.FirstOrDefault(s => s.Type == "lex")
                ?? body.Searches.FirstOrDefault(s => s.Type == "vec")
                ?? body.Searches[0]).Query ?? "";
            var expandedQueries = body.Searches
                .Select(e => new ExpandedQuery(e.Type ?? "lex", e.Query ?? ""))
                .ToList();

            // Use default collections when none specified
            var effectiveCollections = body.Collections is { Count: > 0 }
                ? body.Collections
                : await s.GetDefaultCollectionNamesAsync();

            var results = await s.SearchStructuredAsync(expandedQueries,
                new StructuredSearchOptions
                {
                    Collections = effectiveCollections,
                    Limit = body.Limit ?? 10,
                    MinScore = body.MinScore ?? 0,
                    Intent = body.Intent,
                });

            var formatted = results.Select(r =>
            {
                var snippet = SnippetExtractor.ExtractSnippet(
                    r.BestChunk ?? r.Body, primaryQuery, 300, r.BestChunkPos, intent: body.Intent);
                return new
                {
                    docid = $"#{r.DocId}",
                    file = r.DisplayPath,
                    title = r.Title,
                    score = Math.Round(r.Score * 100) / 100,
                    context = r.Context,
                    snippet = TextUtils.AddLineNumbers(snippet.Snippet, snippet.Line),
                };
            });

            return Results.Json(new { results = formatted }, JsonOpts);
        }

        app.MapPost("/query", HandleSearchEndpoint);
        app.MapPost("/search", HandleSearchEndpoint);

        app.Urls.Add($"http://localhost:{port}");

        Console.Error.WriteLine($"QMD MCP server listening on http://localhost:{port}");
        await app.RunAsync(ct);
    }

    // Request model for POST /query and POST /search
    private class SearchRequest
    {
        public List<SearchEntry>? Searches { get; set; }
        public List<string>? Collections { get; set; }
        public int? Limit { get; set; }
        public double? MinScore { get; set; }
        public string? Intent { get; set; }
    }

    private class SearchEntry
    {
        public string? Type { get; set; }
        public string? Query { get; set; }
    }
}
