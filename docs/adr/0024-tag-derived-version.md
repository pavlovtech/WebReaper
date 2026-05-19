# Derive the package version from the git tag — delete the last manual bump, and the failure mode it carries

## Status

**Proposed** — design pass. One fork is deliberately left open for the
maintainer to grill: how an **additive single-satellite** release expresses a
version divergence once there is no `<Version>` to override (see *The grilling
fork*). No implementation has landed; ADR-0023's `Directory.Build.props`
single-source change (the same PR as this ADR) is the accepted interim and
stands on its own regardless of how this resolves.

## Context

A WebReaper release is two acts: **bump the version**, then **tag**. The tag
half is automated and now driven for the maintainer (push `v<X>` →
`release.yml` → one approval click). The bump half is still manual, and it has
a recurring, concrete cost — not a hypothetical:

- The version is a value with **no single owner tied to the act of
  releasing**. ADR-0022 and ADR-0023 were doc/architecture majors whose PRs
  deliberately did not touch `<Version>`; both **stranded the bump**, forcing
  a separate prep PR (#75 for 8.0.0, #77 for 9.0.0) and a human noticing the
  gap. The same class of mistake, twice, two releases running.
- It was also seven hand-edited `<Version>` lines with lockstep-skew risk.

ADR-0023's release wave introduced one fix already (shipped with this ADR's
PR): a single `<Version>` in `Directory.Build.props`, and `release.yml`
selecting by the **effective** MSBuild `Version` (`-getProperty:Version`)
rather than a csproj text grep. That makes lockstep *structural* (skew
unrepresentable) and collapses seven edits to one. **It does not eliminate the
manual bump, nor the stranding failure mode**: a doc-only major PR can still
merge without touching that one line, and the release is still gated on a
human remembering to bump it before tagging.

Apply the project's own deletion test to the `<Version>` value: delete it
entirely and ask what reappears. Nothing a *consumer* needs reappears (the
shipped version is whatever was released). What reappears is purely the
*release ritual* — and that ritual is the thing that broke twice. A value
whose only job is to be hand-synced to an act that already has an
authoritative artifact (the git tag) is duplication with drift, the exact
smell every prior WebReaper ADR exists to kill (ADR-0005's connection pool,
ADR-0008's one serialization grammar, ADR-0023's one documented surface).

## Decision

**Derive `Version` from the git tag at build time** — the tag becomes the
single source; there is no `<Version>` to bump, strand, or skew.

- Adopt a tag→version deriver: **MinVer** (zero-config: reads
  `git describe`; `v9.0.0` ⇒ `9.0.0`, commits after a tag ⇒ a deterministic
  pre-release `9.0.1-alpha.0.N`) or **Nerdbank.GitVersioning** (a committed
  `version.json` + height; more control, more config). One PackageReference
  in `Directory.Build.props`; the `<Version>` line is deleted.
- The release act collapses to exactly what is already automated and driven:
  **push `v<X>`**. No prep PR, no bump to strand — the failure mode behind
  #75 / #77 is removed *by construction*, not by a checklist.
- `release.yml` is unaffected in spirit: it already resolves the **effective**
  `Version` (ADR-0023's `-getProperty:Version` step); with a deriver that
  value comes from the tag. Untagged CI builds get a deterministic
  pre-release version (fine — CI never pushes).

## The grilling fork (the one open question)

With a tag-derived version, **every project shares the tag's version**.
That is exactly right for the lockstep wave (the norm — ADR-0022, ADR-0023)
but it removes the mechanism the documented **additive single-satellite**
release used: a satellite's `.csproj` overriding `<Version>` (e.g.
`WebReaper.Sqlite 7.1.0` while the six stay `7.0.0`, depending on the
already-published `WebReaper 7.0.0`). Options, to grill:

1. **Scoped tags.** `v9.1.0` = lockstep wave; `sqlite-v7.1.0` = that one
   satellite. `release.yml`'s select step keys the package set off the tag
   shape. Keeps both modes first-class; cost is a tag grammar and slightly
   more `release.yml` logic. (Leading candidate.)
2. **`workflow_dispatch` input.** Lockstep via tag; single-satellite via a
   manual workflow run naming the package + version. Keeps tags simple; the
   single-satellite path becomes a deliberate manual action (acceptable — it
   is already the rare, deliberate case).
3. **Lockstep-only.** Drop additive single-satellite entirely; every release
   is the whole set at the tag version. Simplest, but it discards a
   documented, used capability (Sqlite shipped exactly this way at 7.1.0) —
   needs the maintainer's explicit call that it is no longer wanted.

This fork is *why this is a proposed ADR and not folded into the
`Directory.Build.props` PR*: it revisits the runbook's deliberate version-set
design and trades a real capability, so it deserves the grilling, not a
silent swap.

## Considered options

- **`Directory.Build.props` single source, manual bump retained (chosen as
  the interim; shipped now).** Strictly better than seven hand-edits;
  lockstep structural; release.yml reads the effective version. Does not
  remove the bump or the stranding — hence this ADR.
- **MinVer tag-derived (this ADR's recommendation).** Zero-config, the tag is
  the source, the bump and its failure mode are gone. Pre-release versioning
  for untagged builds is deterministic and conventional.
- **Nerdbank.GitVersioning.** More capable (committed `version.json`, build
  height, public/non-public). More moving parts than MinVer for a repo whose
  need is "the tag is the version"; consider only if the scoped-tag fork
  wants `version.json`-driven package sets.
- **Workflow commits the bump (rejected earlier, still rejected).** A bot
  commit to `master` per release; the runbook deliberately keeps the
  workflow from editing versions. Adds history noise and a new failure
  surface to remove a problem the tag already solves.
- **Status quo + discipline (rejected).** "Always bump in the shipping PR"
  is exactly the discipline that failed twice. Process discipline is not a
  fix for a missing single source.

## SemVer / impact

Build and release infrastructure only — **no public API or behavioural
change**, no consumer impact. The guardrail (whole-solution build, unit +
satellite suites, the Native-AOT smoke publish) is unchanged; a tag-derived
version must show the same effective `Version` for a tagged build that the
`Directory.Build.props` value shows today (the acceptance check).

## References

- `docs/RELEASE-RUNBOOK.md` — the version-set selection and "does not edit
  versions" stance this ADR's interim already updated and this proposal would
  complete.
- The #75 / #77 stranded-bump prep PRs — the recurring incident this removes
  by construction.
- ADR-0023 — the release-wave + `Directory.Build.props` single-source +
  effective-`Version` selection this builds on.
- ADR-0005 / ADR-0008 — the "one owner, no duplication-with-drift" precedent
  this applies to the version value.
