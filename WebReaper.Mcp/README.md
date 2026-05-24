# WebReaper.Mcp

MCP (Model Context Protocol) server satellite for [WebReaper](https://github.com/pavlovtech/WebReaper).

The agent client (Cursor / Claude Desktop / Copilot Studio) spawns
this binary and communicates over stdio. Three tools:

- **`scrape`** — fetch a URL and return its main content as LLM-ready Markdown.
- **`map`** — discover URLs on a site via sitemap.xml + root-page links.
- **`extract`** — extract structured fields from a URL using a JSON schema.

The CLI ([ADR-0043](../docs/adr/0043-cli-and-agent-skill.md)) is the
*primary* agent surface; this MCP satellite is the **interop adapter**
for clients that speak MCP only.

## Install

```bash
dotnet tool install --global WebReaper.Mcp
```

Or build from source:

```bash
dotnet build WebReaper.Mcp/WebReaper.Mcp.csproj -c Release
```

## Configure your MCP client

### Claude Desktop

Edit `~/Library/Application Support/Claude/claude_desktop_config.json`
(macOS) or `%APPDATA%\Claude\claude_desktop_config.json` (Windows):

```json
{
  "mcpServers": {
    "webreaper": {
      "command": "dotnet",
      "args": ["WebReaper.Mcp.dll"]
    }
  }
}
```

### Cursor

Edit `~/.cursor/mcp.json`:

```json
{
  "mcpServers": {
    "webreaper": {
      "command": "WebReaper.Mcp",
      "args": []
    }
  }
}
```

(Adjust the `command` to the absolute path of the installed binary.)

## Why prefer the CLI / Skill over MCP?

Per the WebReaper repositioning plan, the **CLI** is ~35× cheaper than
MCP per token (no tool-schema payload, no JSON-RPC wrapping). For
agents that can run shell commands (Claude Code, etc.), prefer
`webreaper init` (the agent skill) over wiring MCP.
