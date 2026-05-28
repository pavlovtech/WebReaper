using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using WebReaper.IntegrationTests.Fixtures;
using WebReaper.TestServer;
using Xunit;
using Xunit.Abstractions;

namespace WebReaper.IntegrationTests;

/// <summary>
/// End-to-end coverage of the MCP server satellite (ADR-0049). The real server
/// binary (copied into this test's output via the WebReaper.Mcp ProjectReference)
/// is spawned over stdio and driven with the official MCP SDK client — a true
/// initialize → tools/list → tools/call handshake, not a hand-rolled JSON-RPC
/// frame. Each tool runs against the deterministic local site. Tagged Mcp so
/// the gate skips it (subprocess + protocol startup per test).
/// </summary>
[Collection("LocalSite")]
[Trait("Category", "Mcp")]
public sealed class McpServerTests
{
    private readonly LocalTestSite _site;
    private readonly ITestOutputHelper _output;

    public McpServerTests(LocalSiteFixture fixture, ITestOutputHelper output)
    {
        _site = fixture.Site;
        _output = output;
    }

    private static async Task<McpClient> StartClientAsync()
    {
        // Run the server from its OWN build output (self-consistent
        // runtimeconfig.json + deps.json). The copy that lands in this test's
        // output dir can't resolve the server's exact dependency set
        // (Microsoft.Extensions.Hosting 9.0.0) under the test's deps.json.
        var mcpDll = ResolveMcpServerDll();

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "WebReaper",
            Command = "dotnet",
            Arguments = [mcpDll],
        });
        return await McpClient.CreateAsync(transport);
    }

    private static string ResolveMcpServerDll()
    {
        var baseDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(baseDir);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WebReaper.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var sep = Path.DirectorySeparatorChar;
        var config = baseDir.Contains($"{sep}Release{sep}") ? "Release" : "Debug";
        var mcpDll = Path.Combine(dir!.FullName, "WebReaper.Mcp", "bin", config, "net10.0", "WebReaper.Mcp.dll");

        Assert.True(File.Exists(mcpDll), $"MCP server dll not found at {mcpDll}");
        return mcpDll;
    }

    private static string FirstText(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().First().Text;

    [Fact]
    public async Task Server_exposes_scrape_map_and_extract_tools()
    {
        await using var client = await StartClientAsync();

        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToHashSet();

        Assert.Contains("scrape", names);
        Assert.Contains("map", names);
        Assert.Contains("extract", names);
    }

    [Fact]
    public async Task Scrape_tool_returns_markdown_for_the_page()
    {
        await using var client = await StartClientAsync();

        var result = await client.CallToolAsync(
            "scrape",
            new Dictionary<string, object?> { ["url"] = _site.Url("/static") },
            cancellationToken: CancellationToken.None);

        Assert.Contains("Widget Pro 3000", FirstText(result));
    }

    [Fact]
    public async Task Map_tool_returns_discovered_urls()
    {
        await using var client = await StartClientAsync();

        var result = await client.CallToolAsync(
            "map",
            new Dictionary<string, object?> { ["url"] = _site.BaseUrl },
            cancellationToken: CancellationToken.None);

        Assert.Contains("/static", FirstText(result));
    }

    [Fact]
    public async Task Extract_tool_returns_structured_json()
    {
        await using var client = await StartClientAsync();

        const string schemaJson = """
        { "children": [
            { "field": "title", "selector": ".title" },
            { "field": "price", "selector": ".price" } ] }
        """;

        var result = await client.CallToolAsync(
            "extract",
            new Dictionary<string, object?>
            {
                ["url"] = _site.Url("/static"),
                ["schemaJson"] = schemaJson,
            },
            cancellationToken: CancellationToken.None);

        var text = FirstText(result);
        Assert.Contains("Widget Pro 3000", text);
        Assert.Contains("$49.99", text);
    }
}
