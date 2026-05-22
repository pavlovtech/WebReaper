# `WebReaper.Cli` — the AOT single-binary primitive agent surface; the bundled Agent Skill is its discoverable adapter

## Status

**Accepted — implementation complete** (2026-05-23; landed on branch
`ai-native-wave` off `origin/master`). **Slice 4 of the AI-native wave**
— the [repositioning plan's §2.5](../REPOSITIONING-PLAN.md) commits
to "CLI is the primitive agent surface; Skill and MCP are adapters."
Additive — new project, no Tier-1 break. Ships free, MIT, AOT-clean.

## Context

The plan's §2.5 ([REPOSITIONING-PLAN.md](../REPOSITIONING-PLAN.md)) is
explicit:

> May-2026 evidence (≈ 35× MCP-vs-CLI token overhead;
> progressive-disclosure skills beating large tool-schema payloads;
> this repo's own deferred-MCP-tool mechanism) makes the primitive a
> **CLI**, not an MCP server.

The CLI is the funnel's first deliverable: a single-binary executable
agents and humans both invoke. The agent surface lattice is then:

1. **`WebReaper.Cli`** (this ADR) — the primitive; everything else is
   an adapter over it.
2. **Agent Skill** (this ADR) — a `SKILL.md` + bundled CLI invocations
   that progressive-disclosure-load in coding agents.
3. **`WebReaper.Mcp`** (ADR-0049) — interop adapter for MCP-only
   clients (Cursor, ChatGPT Desktop, Copilot Studio).

This slice ships #1 and #2 in one swing because Skill *is* CLI-shaped
(it consists of CLI invocations); they share an artifact (`SKILL.md`),
and shipping the CLI without a discoverable surface for agents leaves
half the wedge unrealised.

### Shape decisions

The CLI commands map 1:1 to the library's terminal operations — the
firecrawl shape ("every endpoint speaks one thing") with our terminal
lattice:

| Command | Library equivalent | Default output |
|---|---|---|
| `webreaper scrape <url>` | `Crawl(url).AsMarkdown()` | Markdown |
| `webreaper scrape <url> --schema <path>` | `Crawl(url).Extract(schema)` | JSON |
| `webreaper map <url>` | `ScraperEngineBuilder.MapAsync(url)` | one URL per line |
| `webreaper init` | (writes the Agent Skill) | files |
| `webreaper version` | — | semver string |

Three rules:

- **`scrape` defaults to Markdown.** No schema, no schema file
  required — the firecrawl-shaped wedge ("smallest possible call
  returns LLM-ready text"). A schema, when supplied via `--schema`,
  switches to structured JSON.
- **`map` is its own command, not a flag on `scrape`.** Discovery and
  extraction are different operations (ADR-0042); merging them into
  one command would either flag-overload `scrape` or hide `/map`'s
  ergonomics.
- **`init` is the discoverability vector.** Firecrawl's
  `npx firecrawl-cli@latest init --all --browser` is the funnel
  mechanism worth copying exactly (research digest sharp-observation).
  `webreaper init` writes a `SKILL.md` + a small launcher script into
  the agent's expected location.

### Why a hand-rolled parser, not `System.CommandLine`

`System.CommandLine` 2.0 is preview, still emits `IL2026`/`IL3050`
warnings on AOT publish for several `Bind<T>` paths, and depends on
six transitive packages. The CLI's surface is ~5 commands and ~10
options; a hand-rolled `Dictionary<string, string>`-plus-`switch`
parser is ~120 lines, zero deps, AOT-clean by construction, and
auditable in one sitting. The cost — no auto-completion generation,
no built-in `--help` formatter — is paid by hand: a focused `--help`
formatter is another ~50 lines and reads exactly the way we want.

### Why AOT publish, not framework-dependent

The repositioning plan's headline number — `.NET 10 AOT cold-start
≈ 940 ms vs ≈ 6,680 ms regular .NET 8` — is the funnel claim. The CLI
*is* the AOT proof point. A framework-dependent build would forfeit
the claim and force agent harnesses to install a .NET runtime.

### Skill shape

`SKILL.md` is a progressive-disclosure skill (the same format Claude
Code, Cursor, Windsurf, and Zed all read). Two parts:

1. **Frontmatter / name / description** — what the agent reads first
   to decide whether to load the skill.
2. **Body** — instructions ending in concrete CLI invocations agents
   can run unmodified.

The skill cites the three commands (`scrape`, `map`, `extract` — the
latter as `scrape --schema`) with one-line examples. No tool-schema
JSON dump; the agent reads the markdown and emits the right `bash`
calls. (35× cheaper than MCP per the plan's evidence.)

`webreaper init` writes the skill into `.claude/skills/webreaper/`
(the Claude Code default), with an opt-in flag set for the future
multi-agent rollout. Other agent locations (`.cursor/rules/`,
`.windsurf/`, etc.) are wired when the user opts in via
`--for <agent>`; v1 ships Claude Code by default since that's the
funnel audience this slice targets.

## Decision

Six moves; one new project, one new skill artifact, zero new Tier-1
breakage in the library.

### 1. `WebReaper.Cli` — the new AOT executable project

New project [WebReaper.Cli/](../../WebReaper.Cli/):

- `OutputType=Exe`, `TargetFramework=net10.0`,
  `PublishAot=true`, `InvariantGlobalization=true`,
  `IsAotCompatible=true`, the IL2026-family warnings promoted to
  errors (matching `WebReaper.AotSmokeTest`).
- One `ProjectReference` to `WebReaper`.
- No NuGet dependencies beyond the BCL.

### 2. The argument parser — hand-rolled, ~120 lines

[WebReaper.Cli/Args.cs](../../WebReaper.Cli/Args.cs). Two static
methods: `Parse(string[] args)` returns a tiny record with the
command name, positional args, and a `Dictionary<string, string>` of
flags. `RequireFlag(flags, name)` throws a formatted error if the flag
is missing. No reflection, no expression trees — `switch` on the
command name in `Program.Main`.

### 3. The command handlers — one file per command

- [WebReaper.Cli/Commands/ScrapeCommand.cs](../../WebReaper.Cli/Commands/ScrapeCommand.cs)
- [WebReaper.Cli/Commands/MapCommand.cs](../../WebReaper.Cli/Commands/MapCommand.cs)
- [WebReaper.Cli/Commands/InitCommand.cs](../../WebReaper.Cli/Commands/InitCommand.cs)
- [WebReaper.Cli/Commands/VersionCommand.cs](../../WebReaper.Cli/Commands/VersionCommand.cs)

Each handler builds the library's API surface via
`ScraperEngineBuilder.Crawl(...).AsMarkdown()` /
`Crawl(...).Extract(schema)` / `MapAsync(...)`, prints to stdout or
the `--output <path>` file, and returns an exit code (0 success,
non-zero on argument or runtime errors).

### 4. The bundled skill artifact

[WebReaper.Cli/skill/SKILL.md](../../WebReaper.Cli/skill/SKILL.md).
Embedded as an `EmbeddedResource` in the AOT binary so `init` does
not depend on the working directory layout. The skill text — name,
description, one-paragraph "what is WebReaper?", one-paragraph "when
to use," three example invocations — is the runtime asset.

### 5. `webreaper init` writes the skill

[InitCommand.cs](../../WebReaper.Cli/Commands/InitCommand.cs). Looks
up the embedded skill, writes it to
`.claude/skills/webreaper/SKILL.md` (creating the directory tree).
`--force` overwrites; without it, an existing file is preserved with a
notice. The intent of `--all` (which firecrawl exposes) lands in v2
once the matrix of "where does each agent put its skill?" is
explicitly mapped; v1 is Claude Code.

### 6. Schema input format

`scrape --schema <path>` reads a JSON file. The format is a
minified subset of WebReaper's domain `Schema` shape — `field`,
`selector`, `type`, `attr`, `is_list`, `children`. The CLI parses
JSON via `JsonNode.Parse` (AOT-clean, no reflection-driven
deserialiser) into the library's `Schema`/`SchemaElement` records.
Source-generator-emitted schemas (ADR-0045) emit the *same* JSON
shape, so a code-defined schema and a hand-written CLI schema are
interchangeable.

Example `schema.json`:

```json
{
  "field": "root",
  "children": [
    { "field": "title", "selector": "h1", "type": "string" },
    { "field": "tags",  "selector": ".tag", "type": "string", "is_list": true }
  ]
}
```

### Bounded scope — what this does NOT add

- **`--for cursor` / `--for windsurf`** (multi-agent skill destinations) —
  v2; the matrix of skill conventions across agents is moving and the
  v1 funnel target is Claude Code.
- **`webreaper extract` as a top-level command.** It's `scrape
  --schema`; an alias adds parser surface for no semantic gain.
- **`webreaper crawl` for multi-page** — `scrape` supports
  `--follow <selector>` to add one chain step (the library's
  ADR-0001 chain via the builder), but a full deep-crawl command with
  pagination / sink files is two commands' worth of surface and
  deferred.
- **Auto-update / version checks.** Out of scope; the user runs `dotnet
  tool update` / re-downloads.
- **Completions / man page generation.** Deferred until `--help`
  surfaces user friction.

## Considered options

### (a) `System.CommandLine` 2.0 — rejected

Preview, AOT-unclean (IL2026/IL3050 on `Bind<T>` paths), six
transitive deps. The CLI surface is small enough that the
hand-rolled parser is the cheaper and more honest choice.

### (b) Ship the skill as a separate published package — rejected

Adds release surface (a second NuGet, a separate version-bump cycle).
Embedded in the CLI binary, the skill ships and version-locks with the
tool that consumes it.

### (c) `webreaper` invokes the engine via the hosted API by default — rejected

The plan's eventual `--remote` flag (REPOSITIONING-PLAN §2.5) routes
through the hosted service; v1 runs the engine locally. The hosted
service does not yet exist; the funnel is the OSS engine.

### (d) Multiple subcommand verbs (`webreaper read`, `webreaper get`) — rejected

`scrape` is the firecrawl-aligned verb; `read` / `get` add no clarity
and split discovery across multiple names.

### (e) Auto-detect agent (`webreaper init --auto`) — rejected (deferred)

Cleverness with low payoff: agents that have a `.claude/`, `.cursor/`,
or `.windsurf/` directory next to the CLI are easy to detect, but the
detection logic is asymmetric (one directory might be present without
being the "primary" agent). v1 ships Claude Code; v2 makes the choice
explicit with `--for`.

### (f) Roslyn source-generator for parsing — rejected

A generator that emits `Parse(args)` from attributed handlers is
elegant but adds a build-time analyzer dep, more compile time, and a
shape that v2's `--for` would change. The hand-rolled `switch` is
straightforward and trivially extensible.

## Consequences

- **The funnel has its primitive agent surface.** `webreaper scrape
  https://example.com` returns LLM-ready Markdown to stdout, no
  schema, no setup beyond `dotnet tool install`. The single-binary
  publish is the headline AOT artifact.
- **The Agent Skill ships with the CLI.** `webreaper init` is the
  discoverability mechanism the funnel needs — one command in a new
  repo, the agent now knows about WebReaper.
- **ADR-0049 (MCP) is purely interop.** The CLI is the wedge; MCP
  serves the audience the CLI/skill can't reach (some agent frameworks
  speak MCP only).
- **AOT-clean by construction.** No reflection, no `dynamic`, no
  third-party packages. The `WebReaper.Cli`'s `PublishAot=true`
  publish gates on the same IL warning set as `WebReaper.AotSmokeTest`.
- **No new Tier-1 library surface.** The CLI consumes only the public
  builder surface (`ScraperEngineBuilder.Crawl`, `.MapAsync`, etc.);
  changes inside it cannot break the library, and changes to the
  library's documented public surface break the CLI loudly at compile
  time.
- **CONTEXT.md** gains a one-paragraph mention of the CLI; README gets
  a getting-started block.

## Implementation

Landed on `ai-native-wave`:

1. **`WebReaper.Cli/WebReaper.Cli.csproj`** — new project, AOT-clean,
   references `WebReaper`.
2. **`WebReaper.Cli/Program.cs`** — `Main(string[] args)` dispatches
   to one handler per command via a `switch`.
3. **`WebReaper.Cli/Args.cs`** — minimal hand-rolled parser.
4. **`WebReaper.Cli/Commands/{Scrape,Map,Init,Version}Command.cs`** —
   one handler per command.
5. **`WebReaper.Cli/skill/SKILL.md`** — embedded resource, the skill
   `init` writes.
6. **`WebReaper.Cli/Help.cs`** — `--help` formatter.
7. **`WebReaper.sln`** — adds `WebReaper.Cli` to the solution.
8. **`WebReaper.Tests/WebReaper.Cli.Tests/`** — new test project, ten
   cases pinning arg parsing, command dispatch, help text, file
   output, and the `init` write-skill behaviour (using a temp dir).
9. **CONTEXT.md / README.md** — terms, getting-started block.

### Guardrails (run on the branch tip)

- `dotnet build WebReaper.sln` — 0 errors; pre-existing warning set
  unchanged (the new CLI project emits CS1591 silently because its
  surface is internal-by-default — the public surface is the
  exit-code-and-stdout interface, not API).
- `dotnet test WebReaper.Tests/WebReaper.UnitTests` — all baseline
  tests pass.
- `dotnet test WebReaper.Tests/WebReaper.Cli.Tests` — all new tests
  pass.
- `dotnet publish WebReaper.Cli -c Release -r osx-arm64
  --self-contained true` — 0 IL/AOT warnings; the published binary
  produces correct output for `webreaper version`, `webreaper map
  ...` (with a stub server), `webreaper scrape ...` (against the
  same fixtures as the unit tests).

## References

- ADR-0040 — the `.AsMarkdown()` terminal the CLI's `scrape` default
  consumes.
- ADR-0041 — the page cache; `scrape --max-age` and
  `--cache-size` route here.
- ADR-0042 — the `ISiteMapper` seam the CLI's `map` consumes.
- ADR-0049 — the MCP server; an interop adapter over the same
  builders the CLI uses.
- REPOSITIONING-PLAN §1, §2.5 — the CLI is the primitive agent
  surface, Skill and MCP are adapters; the AOT cold-start claim this
  CLI cashes.
- firecrawl docs (docs.firecrawl.dev/sdks/cli) — `init --all` is the
  funnel-installation mechanism this ADR copies (single-agent in v1).
