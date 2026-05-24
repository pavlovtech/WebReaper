# `WebReaper.Mcp` — MCP server satellite exposing scrape/map/extract as MCP tools

## Status

**Accepted — implementation complete** (2026-05-23; landed on branch
`ai-native-wave` off `origin/master`). **Slice 10 of the AI-native wave**
— the final slice. Per [REPOSITIONING-PLAN §2.5](../REPOSITIONING-PLAN.md):
"MCP server (`WebReaper.Mcp` / `.AspNetCore`), free OSS, *interop
adapter*. Kept for managed/cross-client interop (Cursor/ChatGPT/Copilot
Studio); thin facade over the CLI/builders." Folds into the unreleased
10.0.0 wave; ships free, MIT.

## Context

The repositioning plan §2.5 is explicit about MCP's role: **the CLI is
the primitive**; MCP is the **interop adapter** for clients that speak
MCP only (Cursor, ChatGPT Desktop, Copilot Studio, the broader MCP-only
agent ecosystem). The plan's evidence:

> May-2026 evidence (~35× MCP-vs-CLI token overhead;
> progressive-disclosure skills beating large tool-schema payloads;
> this repo's own deferred-MCP-tool mechanism) makes the primitive a
> **CLI**, not an MCP server.

The MCP satellite therefore ships *thin*. It is not the headline wedge;
it does not try to be feature-complete; its job is to be present on
agent surfaces where CLI/Skill cannot reach.

### What the satellite ships

Three tools mirroring the CLI's three commands:

| MCP tool | Library function |
|---|---|
| `scrape` | `Crawl(url).AsMarkdown()` or `Crawl(url).Extract(schema)` |
| `map` | `ScraperEngineBuilder.MapAsync(url, options)` |
| `extract` | `scrape --schema X` (the typed-JSON variant) |

Each MCP tool is a `[McpServerTool]`-attributed method invoking the
existing library API; the parameters mirror the CLI flags.

The SDK shape (`ModelContextProtocol` C# package, ~0.9.0-preview at
plan time per REPOSITIONING-PLAN §2.5) is preview — the plan flags
"pre-1.0 churn contained to this one non-load-bearing adapter." The
satellite pin-references the SDK version it builds against; major SDK
breaks get caught in the satellite's CI, not the core's.

### Transport choice — stdio

MCP servers run two transports: stdio (the standard for local-spawned
servers — Cursor, Claude Desktop, Copilot Studio) and SSE / HTTP (for
hosted remote servers). v1 ships **stdio** — by far the dominant
shape today; a future `WebReaper.Mcp.AspNetCore` would add HTTP for
hosted-server scenarios, but the wedge is local-agent-spawned MCP.

### Bounded scope

- **No streaming.** Single-shot scrape / map / extract per call.
- **No persistent state across calls.** Each tool invocation builds
  and runs a Crawl, returns the result, exits — no shared resources
  between calls.
- **No authentication / cookies.** Future enhancement; the satellite
  cannot wire complex auth without a config interface.
- **Standard transport (stdio).** HTTP for hosted servers is a future
  `WebReaper.Mcp.AspNetCore` ADR.
- **No `webreaper init` for MCP destinations.** Cursor / ChatGPT
  Desktop / Claude Desktop each have their own MCP config format
  (mcp.json or app-specific JSON); v1 documents the manual wire-up.
  Auto-config is a future v2.

## Decision

One satellite, two files (project + tools class), one tested.

### 1. `WebReaper.Mcp` — new satellite project

[WebReaper.Mcp/](../../WebReaper.Mcp/). Per ADR-0009 satellite pattern:
heavy deps quarantined, core stays dependency-light + AOT-clean.
Depends on `ModelContextProtocol`, `Microsoft.Extensions.Hosting`,
`WebReaper` (core).

`Exe` output — the satellite IS the MCP server binary the agent
client spawns. No PublishAot in v1 (the MCP SDK has reflection paths;
ADR-0009 quarantine).

### 2. `WebReaperTools` — the static tools class

[WebReaper.Mcp/WebReaperTools.cs](../../WebReaper.Mcp/WebReaperTools.cs).
Three `[McpServerTool]`-attributed static methods. Each method:

1. Validates the arguments (URL non-empty, etc.).
2. Builds a Crawl using the library's existing fluent API.
3. Runs it with a stop-after-one-page configuration.
4. Returns the result as a string (Markdown for `scrape`, JSON for
   `extract`, newline-separated URL list for `map`).

### 3. `Program.cs` — the stdio host

[WebReaper.Mcp/Program.cs](../../WebReaper.Mcp/Program.cs). Standard
MCP SDK boilerplate:

```csharp
var builder = Host.CreateApplicationBuilder();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
```

### Bounded scope reminders

- The satellite is the **MCP** server only; the agent-facing
  *contract* is the `WebReaperTools` methods + their parameter docs.
- The CLI (ADR-0043) is the primitive surface; this satellite is the
  adapter. Agents that can call CLI (Claude Code via the Agent Skill
  ADR-0043 ships) shouldn't reach for MCP.

## Considered options

### (a) Bake MCP into the core — rejected

Heavy deps (the MCP SDK and the hosting framework) violate ADR-0009's
"core stays dependency-light + AOT-clean." Satellite is the right
home.

### (b) Ship `WebReaper.Mcp.AspNetCore` with HTTP transport in v1 — rejected (deferred)

The v1 wedge is local-spawned MCP (Cursor / Claude Desktop / Copilot
Studio); HTTP is for hosted remote servers, a smaller audience. The
ASP.NET Core satellite is a clean future addition.

### (c) Make MCP the primary agent surface — rejected

The plan §2.5 says explicitly the *CLI* is the primitive (35× cheaper).
MCP is the interop adapter for clients that cannot reach the CLI.

### (d) Auto-install MCP into agent configs via `webreaper init` — rejected (deferred)

Useful but the per-agent config formats (Cursor mcp.json, Claude
Desktop JSON, Copilot Studio config) shift; v1 documents the manual
wire-up in the satellite's README and revisits auto-install in v2.

## Consequences

- **The repositioning plan §2.5 ships in full.** CLI primitive + Skill
  adapter (ADR-0043) + MCP adapter (this ADR) — the three-tier agent
  surface lattice is complete.
- **MCP-only clients are reachable.** Cursor / Claude Desktop / etc.
  can call WebReaper without spawning a CLI.
- **Pre-1.0 SDK churn is quarantined.** The satellite pin-references
  the SDK version; SDK breaks land in the satellite's CI, not the
  core. The plan's risk note ("Medium likelihood / Low impact —
  thin facade") holds.
- **Hosted (HTTP) MCP is a clean future addition.**
  `WebReaper.Mcp.AspNetCore` ships when a hosted-server caller
  surfaces.
- **CONTEXT.md** gains an **MCP server** term + relationship line.

## Implementation

Landed on `ai-native-wave`:

1. **`WebReaper.Mcp/WebReaper.Mcp.csproj`** — Exe satellite, pins
   `ModelContextProtocol` to a known preview version.
2. **`WebReaper.Mcp/Program.cs`** — stdio host.
3. **`WebReaper.Mcp/WebReaperTools.cs`** — three `[McpServerTool]`s
   wrapping the library.
4. **`WebReaper.Mcp/README.md`** — manual wire-up instructions
   (Claude Desktop / Cursor sections).

### Guardrails

- `dotnet build WebReaper.Mcp` — 0 errors.
- `WebReaper.AotSmokeTest` — unchanged (satellite isn't AOT-required;
  the consumer's MCP runtime makes that choice).
- No unit tests in v1 — the satellite's surface is dominantly SDK
  boilerplate; the value is the library glue, and the library is
  tested elsewhere. Future ADR may add satellite integration tests
  via the SDK's `Microsoft.Extensions.AI` test harness.

## References

- ADR-0009 — registration-seam + satellite pattern; this satellite's
  shape.
- ADR-0042 — `ISiteMapper`; the `map` MCP tool's library backend.
- ADR-0040 — Markdown extractor; the `scrape` MCP tool's default.
- ADR-0043 — CLI + Agent Skill; the primary agent surface this
  satellite complements.
- REPOSITIONING-PLAN §2.5 — the "CLI primitive, Skill and MCP
  adapters" decision this ADR cashes.
- MCP C# SDK: github.com/modelcontextprotocol/csharp-sdk.
