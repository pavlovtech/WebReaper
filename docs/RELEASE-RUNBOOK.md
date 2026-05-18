# WebReaper release runbook

How to cut a WebReaper release: the core `WebReaper` package and its
ADR-0009 satellites.

## Automated path (primary)

`.github/workflows/release.yml` automates everything below. Push an
annotated tag `v<VERSION>` on the merged ship commit (or run the workflow
manually with `dry_run` to rehearse). The workflow then:

- selects exactly the packages whose `.csproj <Version>` equals `<VERSION>`
  (see *Package set* below), builds Release, runs the full guardrail and
  packs — all reversible;
- **pauses on the `nuget-release` Environment for a required-reviewer
  approval** — this is the Phase 2 HARD STOP, preserved as a one-click gate
  before anything irreversible;
- on approval pushes core first, waits for its flat-container index, then
  the rest (`--skip-duplicate`), verifies every id is indexed, and creates
  the GitHub release from the `CHANGELOG.md` section.

One-time owner setup: a nuget.org key scoped **"Push new packages and
package versions" + Glob `WebReaper*`** (Lesson #1) stored as the repo
secret `NUGET_API_KEY`; and Settings → Environments → `nuget-release` with
the owner as a **Required reviewer**. The key then lives once as a secret —
no per-release `~/.nuget-key` placement.

The phases below remain the **authoritative description of the logic the
workflow ports** and the **manual fallback** if CI is unavailable.

## Package set (version-set selection)

| Package | Notes |
|---|---|
| `WebReaper` | core; pushed **first** |
| `WebReaper.Cosmos` | satellite |
| `WebReaper.Mongo` | satellite |
| `WebReaper.Redis` | satellite |
| `WebReaper.AzureServiceBus` | satellite |
| `WebReaper.Puppeteer` | satellite |
| `WebReaper.Sqlite` | satellite (added after the 7.0.0 wave) |

A release publishes exactly the candidates whose `.csproj <Version>` equals
`<VERSION>` — **not** an unconditional all-seven push. A lockstep bump (all
at one version) publishes all of them; an **additive single-satellite**
release (e.g. `WebReaper.Sqlite` `7.1.0` while the six stay `7.0.0`, its
dependency on the already-published `WebReaper 7.0.0`) publishes just that
one. Both are first-class; the workflow's selection step and the manual
phases below both follow this rule.

Replace `<VERSION>` below with the release version (e.g. `7.1.0`) and
`<SHA>` with the merged commit that ships. The packages that ship are those
whose `.csproj <Version>` already equals `<VERSION>` on that commit — the
runbook does not edit versions.

> **Phases 0–2 are reversible. Phase 3 is the point of no return.** nuget.org
> has no hard delete (see [Rollback reality](#rollback-reality)). Phase 2's
> hard stop — not Phase 3 — is the real decision gate.

---

## Phase 0 — Preconditions (gates, no action)

- [ ] The release commit is merged to `master`; no release-blocking PR open.
      `git rev-parse HEAD` is `<SHA>` and `git status --porcelain` shows only
      known-untracked files (never stage `.claude/settings.json`,
      `docs/REPOSITIONING-PLAN.md`, `research/`).
- [ ] **API key scope — the load-bearing precondition.** The five satellite
      IDs are *created* by their first push, so the key must be allowed to
      create new IDs. On nuget.org → API Keys → **Create** (a new key — scope
      is immutable on an existing key; *refresh* preserves the old scope):
  - **Select Scopes → Push**, with **“Push new packages and package
    versions.”** *Not* “Push only new package versions” — that pushes new
    versions of **existing** IDs only, so the core push (existing
    `WebReaper`) succeeds while every brand-new satellite ID returns
    **403**.
  - **Select Packages → Glob Pattern → `WebReaper*`** (matches `WebReaper`
    and all `WebReaper.*`). *Not* an explicit package list — the list only
    shows already-existing IDs, so first-time satellite IDs cannot be
    selected. Per the [nuget.org docs](https://learn.microsoft.com/en-us/nuget/nuget-org/scoped-api-keys),
    a glob also authorizes future new IDs that match it.
  - Same nuget.org account that already owns `WebReaper`.
- [ ] `~/.nuget-key` placed by the release owner, `chmod 600`, containing
      only that key, no trailing newline:
      `printf %s '<KEY>' > ~/.nuget-key && chmod 600 ~/.nuget-key`.
      It is never read or printed before Phase 3, and is `trap`-deleted on
      exit (success or failure). Any retry needs it re-placed.
- [ ] The release owner accepts that `<VERSION>` is **irreversible** per ID
      on nuget.org once pushed.

---

## Phase 1 — Build the exact shipping artifact (reversible)

```bash
cd <repo>
git checkout master && git pull --ff-only
git rev-parse HEAD            # == <SHA> — this is what ships
git status --porcelain        # only the known-untracked allowed
dotnet clean WebReaper.sln -c Release --nologo
dotnet build WebReaper.sln -c Release --nologo   # MUST be 0 errors
```

Warnings are fine (pre-existing CS1591/CS8618/analyzer noise); **0 errors**
is the bar. Then the full **guardrail on this commit** (not a PR branch):

```bash
# Unit + 5 satellite test projects (IntegrationTests excluded — live/flaky)
for t in WebReaper.UnitTests WebReaper.Cosmos.Tests WebReaper.Mongo.Tests \
         WebReaper.Redis.Tests WebReaper.AzureServiceBus.Tests WebReaper.Puppeteer.Tests; do
  dotnet test "WebReaper.Tests/$t/$t.csproj" -c Release --no-build --nologo
done

# Native-AOT publish + smoke (CI uses linux-x64; locally use the host RID)
dotnet publish WebReaper.Tests/WebReaper.AotSmokeTest/WebReaper.AotSmokeTest.csproj \
  -c Release -r <rid> --nologo                 # MUST be zero IL warnings
./WebReaper.Tests/WebReaper.AotSmokeTest/bin/Release/net10.0/<rid>/publish/WebReaper.AotSmokeTest
  # MUST print "AOT SMOKE: ALL PASS" and exit 0

# Dependency-light core (run from repo root — no cwd drift)
dotnet list WebReaper/WebReaper.csproj package --include-transitive \
  | grep -iE 'newtonsoft|cosmos|mongodb|stackexchange|servicebus|puppeteer|sharpcompress'
  # MUST be empty — core pulls only AngleSharp/Http/Polly/Logging.Abstractions
```

All green → proceed. Any red → stop; nothing irreversible has happened.

---

## Phase 2 — Pack & inspect (reversible) — the real decision gate

Pack **with build** — *not* `--no-build`. `--no-build` has intermittently
omitted a satellite `.xml` after clean/stash churn and failed with NU5026:

```bash
rm -rf /tmp/wr-rel && mkdir /tmp/wr-rel
for p in WebReaper WebReaper.Cosmos WebReaper.Mongo WebReaper.Redis \
         WebReaper.AzureServiceBus WebReaper.Puppeteer; do
  dotnet pack "$p/$p.csproj" -c Release -o /tmp/wr-rel --nologo
done
```

Assert every item (inspect each `.nupkg` as a zip + its `.nuspec`):

- Exactly **6** `*.nupkg`, **zero** `*.snupkg` (symbol packages out of scope).
- Every `<version>` == `<VERSION>`.
- License `GPL-3.0-or-later` on all 6.
- **Core** `WebReaper.nupkg`: `lib/<tfm>/WebReaper.dll` **and
  `lib/<tfm>/WebReaper.xml`** (post-PR-#52 the doc ships under the
  IDE-resolvable name — **there must be no `API.xml`**), plus root
  `README.md` + `logo.png`.
- **Each satellite**: root `README.md` + `logo.png`; nuspec `<readme>`,
  `<icon>`; nuspec `<dependency id="WebReaper" version="<VERSION>">`; and its
  own `lib/<tfm>/<Name>.xml` (satellites generate docs and suppress CS1591;
  core keeps CS1591 visible as its live doc backlog).

A scripted inspector that fails loudly on any miss is in this runbook's
git history (the Phase 2 step of the release that introduced this file).

**⛔ HARD STOP.** Print a 6-package summary table. The release owner
explicitly authorises the push. **This is the last reversible moment.**

---

## Phase 3 — Push (IRREVERSIBLE, one call, key never printed)

```bash
trap 'rm -f ~/.nuget-key' EXIT
set +x
KEY=$(cat ~/.nuget-key)
SRC=https://api.nuget.org/v3/index.json
DIR=/tmp/wr-rel

# 1) core FIRST
dotnet nuget push "$DIR/WebReaper.<VERSION>.nupkg" --api-key "$KEY" --source "$SRC" --skip-duplicate
#    abort the whole release if this is non-zero (and not a skip-duplicate success)

# 2) wait until core <VERSION> is restorable (closes the satellite->core gap)
for i in $(seq 1 12); do
  curl -s --max-time 20 https://api.nuget.org/v3-flatcontainer/webreaper/index.json \
    | grep -q '"<VERSION>"' && break
  sleep 30
done

# 3) the 5 satellites by EXPLICIT filename (never a glob — ordering/safety)
for f in WebReaper.Cosmos WebReaper.Mongo WebReaper.Redis \
         WebReaper.AzureServiceBus WebReaper.Puppeteer; do
  dotnet nuget push "$DIR/$f.<VERSION>.nupkg" --api-key "$KEY" --source "$SRC" --skip-duplicate
done
```

- `--source` explicit and mandatory; `--skip-duplicate` makes any re-run safe
  (already-pushed → 409 → skipped, not aborted).
- Key is single-use: `trap`-deleted on any exit. Never `echo "$KEY"`, never
  `set -x`. `dotnet nuget push` does not echo the key; still filter any
  captured output before printing.

### Failure handling

- **Core push fails (non-409):** stop, do **not** push satellites. Nothing
  irreversible happened; re-place the key and retry from Phase 3.
- **403 on satellites (core succeeded):** the key scope is wrong — Phase 0's
  rule was not met. Core is **live and permanent** (a legitimate release of
  an existing ID, just briefly ahead of its satellites — low risk, those IDs
  never existed). Create a correctly scoped key (Phase 0), re-place it,
  re-run Phase 3: core 409→skips, satellites create.
- **A satellite fails after others succeeded:** core + the rest are
  permanent; re-place key and re-run — `--skip-duplicate` makes it idempotent.

---

## Phase 4 — Verify, tag, release (only after all 6 are up)

**1. Ground truth (authoritative, no scratch project):**

```bash
for id in webreaper webreaper.cosmos webreaper.mongo webreaper.redis \
          webreaper.azureservicebus webreaper.puppeteer; do
  curl -s "https://api.nuget.org/v3-flatcontainer/$id/index.json" | grep -q '"<VERSION>"' \
    && echo "$id INDEXED" || echo "$id NOT YET"
done
```

**2. Scratch-restore proof** — that a real consumer resolves satellite→core
from nuget.org. **Create the throwaway project verifiably — do not silence
`dotnet new` and assume it worked** (a suppressed failure makes every
`dotnet add` say *"no project"*, which looks like an indexing lag and is
not):

```bash
rm -rf /tmp/wr-verify && mkdir -p /tmp/wr-verify/app && cd /tmp/wr-verify/app
dotnet new console -n app -o .
ls *.csproj || { echo "FATAL: no csproj"; exit 1; }
dotnet add package WebReaper.Cosmos --version <VERSION>
dotnet list package --include-transitive | grep -i WebReaper
#   expect WebReaper.Cosmos <VERSION> (top-level) AND WebReaper <VERSION> (transitive)
cd / && rm -rf /tmp/wr-verify
```

**3. Tag the exact ship SHA and push:**

```bash
git tag -a v<VERSION> <SHA> -m "WebReaper <VERSION> — <one-line summary>"
git ls-remote origin 'refs/tags/v<VERSION>^{}'   # after push: must == <SHA>
git push origin v<VERSION>
```

(For an annotated tag, `git ls-remote refs/tags/v<VERSION>` shows the tag
**object** hash; dereference with `^{}` to verify the **commit**.)

**4. GitHub release** with the CHANGELOG section as notes (bounded extract —
stops at the next `## ` so it can't bleed into the previous version):

```bash
awk '/^## <VERSION>/{f=1;print;next} f&&/^## /{exit} f{print}' CHANGELOG.md > /tmp/notes.md
grep -c '^## ' /tmp/notes.md     # expect 1
gh release create v<VERSION> --verify-tag \
  --title "WebReaper <VERSION> — <summary>" --notes-file /tmp/notes.md
rm -f /tmp/notes.md
```

**5. Hygiene:** confirm `~/.nuget-key` is gone (trap fired), `rm -rf
/tmp/wr-rel`, `git status` clean.

---

## Rollback reality

nuget.org has **no hard delete**. `dotnet nuget delete <id> <VERSION>` only
*unlists*; the version number is burned forever for that ID. The only
recourse for a bad publish is unlist + ship `<VERSION+1>`. That is why
**Phase 2's stop is the actual decision**, not Phase 3.

---

## Lessons baked in (real incidents)

1. **Key-scope 403 on satellites.** A key scoped “push only new versions” or
   to an explicit package list pushes core fine but 403s every new satellite
   ID. Cost a re-key round trip. → Phase 0 now mandates **“Push new packages
   and package versions” + glob `WebReaper*`**, created as a *new* key.
2. **False “still indexing.”** A scratch-restore harness suppressed a failing
   `dotnet new`; every `dotnet add` then errored *"no project"*, misread as
   nuget.org indexing lag. → Phase 4 verifies the `.csproj` exists and uses a
   direct flat-container query as the authoritative availability check.
