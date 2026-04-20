using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Qmd.Core.Mcp;

namespace Qmd.Core.Tests.Mcp;

/// <summary>
/// Integration tests for the MCP HTTP transport.
/// Starts a real HTTP server on an ephemeral port and sends requests.
/// </summary>
[Trait("Category", "Integration")]
public class McpHttpTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private WebApplication app = null!;
    private HttpClient client = null!;
    private string baseUrl = null!;
    private int jsonRpcId = 1;
    private string? sessionId;

    public async ValueTask InitializeAsync()
    {
        var store = McpTestHelper.CreateSeededStore();
        var instructions = await InstructionsBuilder.BuildAsync(store);
        var uptime = Stopwatch.StartNew();

        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(store);
        builder.Services
            .AddMcpServer(options =>
            {
                options.ServerInfo = new() { Name = "qmd", Version = "0.0.0-test" };
                options.ServerInstructions = instructions;
            })
            .WithHttpTransport()
            .WithTools<QmdTools>()
            .WithResources<QmdResources>();

        this.app = builder.Build();
        this.app.MapMcp("/mcp");

        // GET /health — match production server
        this.app.MapGet("/health", () => Results.Json(
            new { status = "ok", uptime = (int)uptime.Elapsed.TotalSeconds }, JsonOpts));

        // Bind to ephemeral port
        this.app.Urls.Add("http://127.0.0.1:0");
        await this.app.StartAsync(TestContext.Current.CancellationToken);

        // Get the actual port
        var address = this.app.Urls.First();
        this.baseUrl = address;

        this.client = new HttpClient { BaseAddress = new Uri(this.baseUrl) };
    }

    public async ValueTask DisposeAsync()
    {
        this.client.Dispose();
        await this.app.StopAsync(TestContext.Current.CancellationToken);
        await this.app.DisposeAsync();
    }

    /// <summary>Send a JSON-RPC request to the MCP endpoint.</summary>
    private async Task<JsonElement> SendMcpRequest(string method, object? @params = null, CancellationToken ct = default)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = this.jsonRpcId++,
            method,
            @params,
        };

        var json = JsonSerializer.Serialize(request, JsonOpts);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Accept.ParseAdd("application/json");
        httpRequest.Headers.Accept.ParseAdd("text/event-stream");
        if (this.sessionId != null)
            httpRequest.Headers.Add("mcp-session-id", this.sessionId);

        var response = await this.client.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();

        // Capture session ID from response
        if (response.Headers.TryGetValues("mcp-session-id", out var sessionValues)) this.sessionId = sessionValues.FirstOrDefault();

        var body = await response.Content.ReadAsStringAsync(ct);

        // MCP Streamable HTTP may return SSE or JSON. Parse accordingly.
        if (body.StartsWith("event:") || body.StartsWith("data:"))
        {
            // Parse SSE — extract the JSON-RPC response from data lines
            var dataLines = body.Split('\n')
                .Where(l => l.StartsWith("data:"))
                .Select(l => l[5..].Trim())
                .Where(l => !string.IsNullOrEmpty(l))
                .ToList();

            foreach (var line in dataLines)
            {
                var parsed = JsonDocument.Parse(line).RootElement;
                if (parsed.TryGetProperty("result", out _) || parsed.TryGetProperty("error", out _))
                    return parsed;
            }

            throw new InvalidOperationException($"No JSON-RPC response found in SSE: {body}");
        }

        return JsonDocument.Parse(body).RootElement;
    }

    [Fact]
    public async Task Health_Returns200WithStatus()
    {
        var response = await this.client.GetAsync("/health", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("status").GetString().Should().Be("ok");
        body.TryGetProperty("uptime", out _).Should().BeTrue();
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        var response = await this.client.GetAsync("/nonexistent", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Initialize_ReturnsServerInfo()
    {
        var result = await this.SendMcpRequest("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test-client", version = "1.0" }
        }, TestContext.Current.CancellationToken);

        result.TryGetProperty("result", out var initResult).Should().BeTrue();
        initResult.GetProperty("serverInfo").GetProperty("name").GetString().Should().Be("qmd");
    }

    [Fact]
    public async Task ToolsList_ReturnsRegisteredTools()
    {
        // Must initialize first
        await this.SendMcpRequest("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test-client", version = "1.0" }
        }, TestContext.Current.CancellationToken);
        await this.SendMcpRequest("notifications/initialized", ct: TestContext.Current.CancellationToken);

        var result = await this.SendMcpRequest("tools/list", ct: TestContext.Current.CancellationToken);

        result.TryGetProperty("result", out var listResult).Should().BeTrue();
        var tools = listResult.GetProperty("tools");
        tools.GetArrayLength().Should().BeGreaterThan(0);

        // Should have our core tools
        var toolNames = tools.EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .ToList();
        toolNames.Should().Contain("query");
        toolNames.Should().Contain("get");
    }

    [Fact]
    public async Task ToolsCall_Query_ReturnsResults()
    {
        // Initialize
        await this.SendMcpRequest("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test-client", version = "1.0" }
        }, TestContext.Current.CancellationToken);
        await this.SendMcpRequest("notifications/initialized", ct: TestContext.Current.CancellationToken);

        var result = await this.SendMcpRequest("tools/call", new
        {
            name = "query",
            arguments = new { query = "readme", limit = 5 }
        }, TestContext.Current.CancellationToken);

        result.TryGetProperty("result", out var callResult).Should().BeTrue();
        var content = callResult.GetProperty("content");
        content.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ToolsCall_Get_ReturnsDocument()
    {
        // Initialize
        await this.SendMcpRequest("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test-client", version = "1.0" }
        }, TestContext.Current.CancellationToken);
        await this.SendMcpRequest("notifications/initialized", ct: TestContext.Current.CancellationToken);

        var result = await this.SendMcpRequest("tools/call", new
        {
            name = "get",
            arguments = new { file = "readme.md" }
        }, TestContext.Current.CancellationToken);

        result.TryGetProperty("result", out var callResult).Should().BeTrue();
        var content = callResult.GetProperty("content");
        content.GetArrayLength().Should().BeGreaterThan(0);

        // Content should include the document text (may be in text or resource block)
        var contentJson = content.GetRawText();
        contentJson.Should().Contain("Project README");
    }
}
