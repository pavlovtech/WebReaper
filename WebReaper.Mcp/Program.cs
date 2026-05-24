// ADR-0049: WebReaper.Mcp — MCP server satellite. The agent client
// (Cursor / Claude Desktop / Copilot Studio) spawns this binary and
// communicates over stdio. Tools live in WebReaperTools.cs.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// MCP servers log to stderr (stdout is the protocol channel).
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
