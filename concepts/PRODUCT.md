# PRODUCT.md: WebReaper Concept Sites

## What this is

Standalone single-page marketing sites for **WebReaper**: an AI-native web scraper shipped as a ~12 MB single-binary CLI, a .NET library (NuGet `WebReaper`), and MCP servers. MIT-licensed, local-first, no accounts. Each site is a self-contained static build in `concepts/<nn-name>/` with a `/guide` route documenting its creative direction, and deploys as its own Vercel project.

**Scope note (2026-07-10):** the original brief explored ten aesthetic lanes; the owner cut scope to the two strongest in-flight concepts. Shipped:

1. `01-tty`, "PHOSPHOR": a literal DEC-terminal CRT. The site is an interactive shell; every product fact is a command. Drenched P1 green (P3 amber togglable), VT323, recorded-real transcripts.
2. `06-pipeline`, "The Reaping Line": a bright workshop configurator. Four stations (scope, transport, extraction, destination) assemble a real CLI command and the equivalent C# builder chain live. Technical-toy aesthetic: dot grid, 2 px ink borders, offset block shadows, one OKLCH role color per station.

Unbuilt lanes (broadsheet, blueprint, field guide, WebGL harvest, manga, Swiss grid, Win-98, mission control) were direction-only; generated art for three of them is preserved in git history (commit f2ed825) if ever revived.

## Audience

Developers (primarily .NET, secondarily anyone building AI agents/pipelines), AI-tooling enthusiasts, and a broad tech audience arriving from social media. They know what a scraper is; they judge craft instantly and distrust hype.

## Register

**Brand**: design IS the product. Each concept commits to one named aesthetic lane and executes it to the hilt; lanes must not overlap each other or the main webreaper.ai site (dark Supabase-style). "Literal register" exceptions are intentional: mono is used because it IS a terminal.

## Voice

Grounded, technically precise, playful in framing but never in facts. Every command, flag, and API call shown must exist in `concepts/_shared/FACTS.md` (verified against the repo and the real release binary). Demos replay recorded output and say so. The reaper/harvest metaphor is the shared thread.

## Constraints

- Static HTML/CSS/JS, self-contained per directory; no build step; Google Fonts allowed.
- Each site: responsive (375px to 1600px), reduced-motion safe, semantic HTML, keyboard-usable interactions, `/guide` route, noscript fallback where content is JS-driven.
- Never touch WebReaper library/product code. No invented features, benchmarks, or pricing. No em-dashes in copy (house style).
