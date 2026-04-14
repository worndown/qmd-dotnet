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
public class McpHttpTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private string _baseUrl = null!;
    private int _jsonRpcId = 1;
    private string? _sessionId;

    public async Task InitializeAsync()
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

        _app = builder.Build();
        _app.MapMcp("/mcp");

        // GET /health — match production server
        _app.MapGet("/health", () => Results.Json(
            new { status = "ok", uptime = (int)uptime.Elapsed.TotalSeconds }, JsonOpts));

        // Bind to ephemeral port
        _app.Urls.Add("http://127.0.0.1:0");
        await _app.StartAsync();

        // Get the actual port
        var address = _app.Urls.First();
        _baseUrl = address;

        _client = new HttpClient { BaseAddress = new Uri(_baseUrl) };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    /// <summary>Send a JSON-RPC request to the MCP endpoint.</summary>
    private async Task<JsonElement> SendMcpRequest(string method, object? @params = null)
    {
        var request = new
        {
            jsonrpc = "2.0",
            id = _jsonRpcId++,
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
        if (_sessionId != null)
            httpRequest.Headers.Add("mcp-session-id", _sessionId);

        var response = await _client.SendAsync(httpRequest);
        response.EnsureSuccessStatusCode();

        // Capture session ID from response
        if (response.Headers.TryGetValues("mcp-session-id", out var sessionValues))
            _sessionId = sessionValues.FirstOrDefault();

        var body = await response.Content.ReadAsStringAsync();

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
        var response = await _client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("ok");
        body.TryGetProperty("uptime", out _).Should().BeTrue();
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        var response = await _client.GetAsync("/nonexistent");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Initialize_ReturnsServerInfo()
    {
        var result = await SendMcpRequest("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test-client", version = "1.0" }
        });

        result.TryGetProperty("result", out var initResult).Should().BeTrue();
        initResult.GetProperty("serverInfo").GetProperty("name").GetString().Should().Be("qmd");
    }

    [Fact]
    public async Task ToolsList_ReturnsRegisteredTools()
    {
        // Must initialize first
        await SendMcpRequest("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test-client", version = "1.0" }
        });
        await SendMcpRequest("notifications/initialized");

        var result = await SendMcpRequest("tools/list");

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
        await SendMcpRequest("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test-client", version = "1.0" }
        });
        await SendMcpRequest("notifications/initialized");

        var result = await SendMcpRequest("tools/call", new
        {
            name = "query",
            arguments = new { query = "readme", limit = 5 }
        });

        result.TryGetProperty("result", out var callResult).Should().BeTrue();
        var content = callResult.GetProperty("content");
        content.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ToolsCall_Get_ReturnsDocument()
    {
        // Initialize
        await SendMcpRequest("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "test-client", version = "1.0" }
        });
        await SendMcpRequest("notifications/initialized");

        var result = await SendMcpRequest("tools/call", new
        {
            name = "get",
            arguments = new { file = "readme.md" }
        });

        result.TryGetProperty("result", out var callResult).Should().BeTrue();
        var content = callResult.GetProperty("content");
        content.GetArrayLength().Should().BeGreaterThan(0);

        // Content should include the document text (may be in text or resource block)
        var contentJson = content.GetRawText();
        contentJson.Should().Contain("Project README");
    }
}
