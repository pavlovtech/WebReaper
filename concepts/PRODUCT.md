# PRODUCT.md — WebReaper Concept Sites

## What this is

Ten fundamentally different single-page marketing sites for **WebReaper** — an AI-native web scraper shipped as a ~12 MB single-binary CLI, a .NET library (NuGet `WebReaper`), and MCP servers. MIT-licensed, local-first, no accounts. Each site is a self-contained static build in `concepts/<nn-name>/` with a `/guide` route documenting its creative direction. They will be deployed as ten separate Vercel projects and shown to a large public audience as a demonstration of design range.

## Audience

Developers (primarily .NET, secondarily anyone building AI agents/pipelines), AI-tooling enthusiasts, and a broad tech audience arriving from social media. They know what a scraper is; they judge craft instantly and distrust hype.

## Register

**Brand** — design IS the product here. Each concept commits to one named aesthetic lane and executes it to the hilt. The ten lanes are deliberately non-overlapping:

1. `01-tty` — literal DEC-terminal phosphor CRT (interactive shell). Drenched green.
2. `02-broadsheet` — literal 1890s newspaper (engravings, columns). Newsprint + iron-gall ink + oxblood.
3. `03-blueprint` — literal diazo/cyanotype engineering drawing. Prussian-blue drench, white linework.
4. `04-field-guide` — literal natural-history specimen monograph. Aged plate paper, botanical-green ink, wax red.
5. `05-harvest` — cinematic WebGL void: wheat-gold particle harvest in black space.
6. `06-pipeline` — light precision tool-canvas (interactive builder). Full palette by pipeline role.
7. `07-manga` — literal B/W manga chapter (halftone, SFX, generated panels). Ink + shock red.
8. `08-grid` — literal Swiss International Typographic Style. White/black/Swiss-red planes, Helvetica.
9. `09-98` — literal Windows-98 desktop OS parody. Teal desktop, silver chrome, navy title bars.
10. `10-ops` — mission-control telemetry HUD. Near-black blue, amber primary, multi-signal accents.

"Literal register" exceptions are intentional: paper is cream because it IS paper; mono is used because it IS a terminal.

## Voice

Grounded, technically precise, playful in framing but never in facts. Every command, flag, and API call shown must exist in `concepts/_shared/FACTS.md` (verified against the repo). Demos replay recorded output and say so. The reaper/harvest metaphor is the shared thread; each concept interprets it in its own world.

## Constraints

- Static HTML/CSS/JS, self-contained per directory; no build step; Google Fonts allowed; three.js vendored where used.
- Each site: responsive (375px → 1600px), reduced-motion safe, semantic HTML, keyboard-usable interactions, `/guide` route.
- Never touch WebReaper library/product code. No invented features, benchmarks, or pricing.
