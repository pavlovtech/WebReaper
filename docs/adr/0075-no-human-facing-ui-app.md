# No separate human-facing UI app: the human onramp is the Agent Skill, not a TUI or desktop GUI

## Status

**Accepted** (2026-05-29). Triggered by a post-v10.0.0 question: should WebReaper
offer an interactive mode, or a separate app with a nicer UI, for a friendlier
first-run human experience? This ADR records the answer (no) and where the
human-facing experience lives instead, so the idea is settled rather than
re-litigated each release.

## Context

The v10.0.0 CLI (`webreaper`, ADR-0043) is a Native-AOT single binary with a
non-interactive, command-driven surface: `scrape`, `map`, `browser`, `stealth`,
`init`, `version`. Run with no command, it prints a usage error and points at
`--help`.

Two adjacent questions came up:

1. **An interactive mode inside the existing binary**: run `webreaper` with no
   args and get a menu / full-screen TUI to pick functions.
2. **A separate app with a nicer UI** for a human first-run experience, sitting
   beside the CLI.

Both are reasonable instincts. The bare binary is unfriendly to a human who
pokes at it without reading docs; double-clicking it in Finder even hits a
Gatekeeper block, because macOS will not GUI-launch a bare Mach-O that is not
packaged as a `.app` (see ADR-0071). The question is whether a richer human UI
belongs in, or beside, the OSS CLI.

## Decision

**WebReaper ships no interactive TUI in the CLI binary, and no separate
human-facing UI app.** The OSS CLI stays a lean, non-interactive, scriptable
agent primitive.

The human-facing experience lives in two places that already exist or are
already planned:

- **Now, free: the Agent Skill** (written by `webreaper init`, ADR-0043).
  "Ask an AI agent to scrape X, and it drives `webreaper scrape` / `map` for
  you" is a conversational, human-friendly front end that needs no UI code, no
  second binary, and no second release pipeline. It is also the project's
  differentiator (AI-native), so leaning on it is on-strategy rather than a
  workaround.
- **Later, if justified: a hosted surface.** A point-and-click human UI, if it
  is ever warranted, belongs as a hosted/managed web surface, not a downloaded
  desktop app (rationale in *Considered & rejected* (d), (e) and the market
  note below).

The only in-CLI interactivity left on the table is a **TTY-gated
prompt-on-missing-args** (the `gh` pattern), deferred until there is real
human-user demand (*Considered & rejected* (b)).

## Considered & rejected

### (a) Interactive TUI inside the `webreaper` binary

Rejected on three grounds; the first is decisive.

- **AOT.** The binary's value proposition includes AOT cold-start, enforced on
  every publish: `WebReaper.Cli.csproj` sets `PublishAot=true`,
  `IsAotCompatible=true`, and promotes the full IL trim/AOT warning set to
  errors. The candidate libraries do not clear that bar cleanly. Terminal.Gui
  has no clearly documented Native-AOT support. Spectre.Console added AOT
  support for output and prompts, but still emits IL warnings in places (for
  example a `TypeConverterHelper` warning reported on net9), and
  `Spectre.Console.Cli` is explicitly not trim/AOT-safe; any residual IL
  warning trips the promote-to-error gate. Adding a TUI means spending effort
  fighting the binary's headline property.
- **Agent-hostile.** The CLI's primary consumer is an AI agent (the Skill, and
  the `WebReaper.Mcp` satellite per ADR-0049). Agents need clean,
  non-interactive, parseable stdin/stdout. A menu that blocks on human
  keystrokes is the wrong shape for the number-one caller.
- **Composability.** Scrapers get piped, scheduled, and run in CI. A TUI does
  not compose. The wider CLI world learned this the hard way: AWS CLI's
  auto-prompt mode broke non-interactive usage, and rclone's interactive-only
  config is a standing complaint, not a loved feature.

### (b) TTY-gated prompt-on-missing-args in the existing CLI (deferred, not rejected)

The `gh` pattern: when a required argument is missing AND stdin is an
interactive TTY, prompt for it; when not a TTY (piped, agent, CI), keep today's
behaviour exactly. This is the one form of interactivity compatible with the
constraints above, because it never fires on the agent or script path. If
built, hand-roll it with `Console.ReadLine` so it adds no dependency and stays
AOT-pure; do not pull a prompt library for it. Deferred until human-user
friction is actually observed; the existing `--help` pointer covers
discoverability for now.

### (c) A separate terminal TUI binary (e.g. `webreaper-tui`)

Sidesteps the AOT objection, since a second binary need not be AOT-published.
Rejected on fit: it falls between two stools. Users comfortable in a terminal
want flags and agent automation, not a menu; users who want a "nice UI" will
not open a terminal at all. It adds a second build, test, and release artifact
for a thin, ill-defined payoff.

### (d) A separate desktop GUI app (Avalonia / Tauri / Electron)

Rejected. This is the no-code, point-and-click quadrant occupied by Octoparse
and ParseHub. WebReaper's entire stack (a .NET library, an AOT CLI, an Agent
Skill, an MCP satellite) is built for code and for agents, not for clicks;
serving the non-developer GUI audience is a different product for a different
market, not a side feature. It also multiplies the release burden: v10.0.0
already required a multi-PR fight with codesigning, notarization,
stapler-on-Mach-O, and cross-OS packaging for one CLI binary (ADR-0070,
ADR-0071). A GUI app means `.app` bundles, installers, auto-update, and that
same gauntlet across three platforms, indefinitely.

### (e) A local web UI (`webreaper serve` opening a localhost dashboard)

Rejected as a standalone, separately-maintained app. It is the most defensible
of the UI options precisely because it is a local preview of a hosted surface;
if such a UI is built, it should be built as that hosted surface's local or
lite form, not as a forever-maintained side binary in the OSS CLI repo. That is
a deliberate product decision to make when a hosted surface is on the roadmap,
not an OSS-CLI feature.

### (f) A selector-debugging REPL (`webreaper shell <url>`, the `scrapy shell` analogue)

The one interactive shape with genuine precedent for a scraper: a REPL to test
extraction selectors against a fetched page during development. Rejected for
this binary specifically, because a REPL needs runtime code evaluation, which
is about as AOT-hostile as it gets. A JIT-based developer tool could offer
this; the AOT CLI is the wrong host.

## Market note: built for code vs built for clicks

The scraper market sorts cleanly into two camps. The no-code camp (Octoparse,
ParseHub) ships point-and-click GUIs for non-developers; the trade-off is
fragility and difficulty integrating into automated pipelines. The code and
AI-native camp (Firecrawl, Apify, and WebReaper) ships API, CLI, and library
surfaces for developers and agents. Notably, the modern code-camp players do
not ship downloadable desktop GUIs at all; where they offer a human UI, it is a
hosted dashboard. A separate WebReaper UI app would mean crossing into the camp
the project is otherwise positioned against, via a delivery mechanism (desktop
download) that even that camp is moving away from.

## What would reverse this decision

A deliberate choice to pursue the no-code / non-developer market as a primary
segment, that is, to compete with Octoparse for point-and-click users. That is
a market-positioning decision, not a feature request, and should be taken
explicitly. Even then, the right delivery is a hosted surface, not a bundled
desktop download, so this ADR's rejection of the desktop-app shape would still
largely hold.

## Consequences

- **The OSS CLI surface stays small and AOT-clean.** No new dependencies, no
  second binary, no added release pipeline. The `WarningsAsErrors` AOT gate
  stays meaningful.
- **The human onramp is the Agent Skill.** Friendliness for non-expert humans
  is delivered conversationally through an AI agent, which is on-strategy
  (AI-native) rather than a UI side-project.
- **The discoverability gap is acknowledged, not closed.** A human running the
  bare binary still gets a terse usage error. The deferred TTY-prompt (b) is
  the sanctioned fix if that friction proves real; until then, `--help` is the
  answer.
- **The idea is settled.** Future "should we add a UI / interactive mode?"
  proposals start from this record, and must either show new demand (b) or
  argue the market pivot (*What would reverse this decision*).

## References

- ADR-0043: `WebReaper.Cli` AOT single-binary CLI and the bundled Agent Skill
  (the human onramp this ADR leans on).
- ADR-0049: `WebReaper.Mcp` satellite (the other agent-facing surface; same
  non-interactive posture).
- ADR-0055: CLI browser/stealth bake policy (precedent for keeping the AOT
  binary's surface lean and AOT-clean).
- ADR-0070: CLI distribution channels (the packaging burden a second app would
  multiply).
- ADR-0071: macOS codesigning and notarization (why a bare CLI will not
  GUI-launch from Finder; why a GUI app's packaging is costly).
- External market framing: Firecrawl vs Octoparse ("built for code vs clicks");
  Apify's web-scraping-tools roundup; Octoparse vs ParseHub no-code
  positioning.
