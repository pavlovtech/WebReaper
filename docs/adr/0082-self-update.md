# 0082. Self-update: an update notifier now, a guarded `update` command later

**Status:** Accepted (design pass 2026-05-30). Designed, not yet built; the notifier targets a near-term minor.
**Date:** 2026-05-30
**Deciders:** Alex (HITL), Claude (design pass)

## Context

The `webreaper` CLI has no in-tool upgrade path. A user who installed it weeks ago has no signal that a newer version exists and, depending on how they installed it, no obvious command to upgrade. That costs the funnel (people stay on stale versions and hit already-fixed bugs) and costs support ("is this fixed?" when it was fixed two releases ago). Firecrawl and the polished CLIs (gh, rustup, deno) all solve this; WebReaper should too.

Three facts shape the design.

1. **The self-update flow already exists, in shell.** `scripts/install.sh` (ADR-0070) performs exactly what a self-update would: resolve the latest tag from `api.github.com/repos/pavlovtech/WebReaper/releases/latest`, download `webreaper-<version>-<rid>.{tar.gz,zip}` from `releases/download`, verify it against the published `SHA256SUMS`, then install atomically (stage next to the target, then `mv -f`). It already supports `--upgrade` (overwrite only if strictly newer) and an idempotency check that shells out to `webreaper version`. The artifact-naming contract and the download/verify/swap sequence are proven; a `webreaper update` command is that flow in C#, or the binary re-running install.sh with `--upgrade`.

2. **The install channels have different, conflicting upgrade stories** (ADR-0070). Homebrew (`pavlovtech/homebrew-webreaper`) owns its binary under the Cellar and upgrades via `brew upgrade`; a self-replace that overwrites that file desyncs brew's metadata, so the next `brew upgrade` clobbers or conflicts. winget is the same on Windows. Only the `curl | sh` install.sh users and the raw GitHub-release downloaders (including Windows manual) have *no* package manager to upgrade them. So self-replace is wrong for some channels and the only option for others.

3. **The CLI's stdout is the payload.** `scrape` / `crawl` / `map` write JSON Lines or Markdown that agents, `>` redirects, and shell pipelines consume. Anything the tool says about updates must go to **stderr** and must be silent in non-interactive, piped, and CI contexts, or it corrupts that payload.

Two supporting facts make self-replace tractable when it is wanted: the release binaries are notarized **and stapled** (ADR-0071), so a self-downloaded macOS binary launches under Gatekeeper without quarantine-attribute gymnastics (which is why install.sh does not strip the xattr); and the CLI is a single Native-AOT binary, so swapping it is a one-file atomic rename with no dependency directory to reconcile.

## Decision

Two parts, deliberately sequenced.

### Part 1 (build now): an update notifier

After a data command (`scrape` / `crawl` / `map`) completes, best-effort check the latest released tag, compare it to the running version, and if a newer one exists print **one stderr line**:

```
webreaper 10.3.0 is available (you have 10.2.0). Upgrade: brew upgrade webreaper
```

It never touches the binary. It is gated hard:

- **Interactive only.** Runs only when stderr is a TTY, not under CI (respect the `CI` env var), and not disabled by `WEBREAPER_NO_UPDATE_CHECK` (also honor the de-facto `NO_UPDATE_NOTIFIER`). Agents and pipelines see nothing.
- **Never on stdout, never blocking, never fatal.** All errors (offline, rate-limited, parse failure) are swallowed; the command's own exit code and output are unaffected. The check runs after the command's real work, with a short timeout (~1.5s).
- **Throttled.** Cache the last-check timestamp and the latest-known tag in `~/.webreaper/update-check.json` (the existing `~/.webreaper/` home). Hit the network at most once per 24h; between checks the cached tag drives the message with no network call.
- **Channel-aware hint.** If the running binary's path is inside a Homebrew or winget prefix, the hint is `brew upgrade webreaper` / `winget upgrade`; otherwise it is the install.sh one-liner (or `webreaper update` once Part 2 ships).

### Part 2 (guarded follow-up): a `webreaper update` self-replace command

Reuses the ADR-0070 artifact contract: resolve the latest (or a pinned) tag, download the current-RID asset, verify it against `SHA256SUMS`, and atomically swap `Environment.ProcessPath`. Either as a C# port of install.sh's flow (reusing the browser/stealth-install HTTP-download helpers) or by shelling out to install.sh `--upgrade` on macOS/Linux (cheaper, reuses the exact tested path; the CLI already self-invokes as a subprocess for stealth install per ADR-0056). Four guards make it safe:

- **Refuse under a package manager (the load-bearing guard).** If the binary path is inside a Homebrew prefix (`brew --prefix` / a `Cellar` path) or a winget/scoop location, do not self-replace. Print "installed via Homebrew, run `brew upgrade webreaper`" and exit non-zero. When the install method is ambiguous, err toward refusing.
- **Permissions.** If the target is not writable (root-owned `/usr/local/bin`), fail clean with guidance (re-run with elevation, or reinstall under `~/.local/bin`); never half-write.
- **Windows atomic replace.** A running `.exe` cannot be overwritten: rename self to `.old`, move the new binary into place, delete `.old` on the next launch. Linux/macOS use the install.sh temp-then-rename pattern.
- **Signature verification.** SHA256 against `SHA256SUMS` is the floor (it matches install.sh and catches corruption). For a self-replacing path, prefer a signature check (minisign or cosign over `SHA256SUMS`) so a compromised release host that rewrites both the binary and its checksums cannot push a trusted update. A signing-key decision is a prerequisite for hardening Part 2; until then Part 2 stays SHA256-only and that limitation is documented.

**Why split.** The notifier is small, channel-agnostic, safe, and funnel-positive, so it ships first. The self-replace command helps only the non-package-manager users and carries the brew-desync, Windows, permissions, and signing complexity, so it is a deliberate second step gated on real demand and the signing-key decision.

## Considered options

- **(a) Unguarded on-by-default self-update.** The canonical CLI-self-update footgun: it desyncs Homebrew, the primary macOS/Linux channel. Rejected; self-replace must detect and refuse under a package manager.
- **(b) Silent auto-update (swap the binary in the background, no prompt).** A supply-chain and agent-trust hazard: an agent or CI step invoking `webreaper` must get a deterministic binary, never a surprise swap mid-pipeline. Rejected; updates are always explicit (the user runs `update` or `brew upgrade`).
- **(c) Notifier on stdout.** Corrupts the JSON Lines / Markdown payload downstream consumers parse. Rejected; stderr-only and TTY-gated.
- **(d) Package-manager-only, no notifier.** Leaves `curl | sh` and raw-download users with no upgrade signal at all. Rejected; the notifier is the cheap win that covers every channel.
- **(e) Notifier on even in CI / pipes.** Log noise and stderr-mixing risk; agents do not need it. Rejected; auto-off unless interactive.
- **(f) A separate bundled updater binary (Squirrel-style).** Over-built for a single AOT file; the install.sh flow plus an atomic rename suffices. Rejected.

## Consequences

Good:
- Every channel gets a correct upgrade story (notifier hint plus, for non-PM users, `webreaper update`). Stale-version support load drops and the funnel improves.
- Little new surface: the notifier reuses the `releases/latest` call install.sh already depends on; `webreaper update` reuses the ADR-0070 artifact contract and the existing browser/stealth-install download helpers.

Costs:
- The notifier's correctness work is entirely in the gating (TTY, CI, env, throttle, timeout, stderr-only). Gated wrong, it becomes the thing that breaks someone's pipe or spams a log.
- `webreaper update` owns the package-manager-detection heuristic (a path-prefix match that can be wrong on unusual layouts, so it errs toward refusing), the Windows rename dance, permission handling, and the signing-key decision.

Reconciles ADR-0070 (the distribution channels and the install.sh flow this builds on) and ADR-0071 (notarization plus stapling, which is what lets a self-downloaded macOS binary launch). This is a CLI-distribution concern, sibling to ADR-0070 / ADR-0071; it touches no crawl-engine concept and adds no term to CONTEXT.md.

## Implementation sketch (not yet built)

- **Notifier:** an `UpdateNotifier` in `WebReaper.Cli` (check + 24h cache file + gate + one stderr line), invoked from `Program.cs` after a successful `scrape` / `crawl` / `map`. AOT-clean (System.Text.Json over the cache file and the `releases/latest` JSON, no reflection). Around 80 lines.
- **`webreaper update`:** a new `UpdateCommand` reusing the existing HTTP-download helpers and the install.sh artifact naming; an install-method-detection helper (brew/winget prefix); the per-OS atomic swap.
- **Tests:** notifier gating (TTY off, CI set, env disabled, inside the throttle window) and the version comparison; install-method detection (a brew-prefix path refuses to self-replace); an offline test of the asset-URL plus SHA256-verification path against a fake release manifest.
- **Semver:** additive (an opt-out notifier and a new `update` subcommand). The notifier's first appearance is a visible behaviour change for interactive users (a new stderr line), so it lands in a minor with a CHANGELOG note.
