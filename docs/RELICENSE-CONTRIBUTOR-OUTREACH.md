# Contributor outreach templates — GPL → MIT relicense (ADR-0017)

> **Status: NOT SENT.** The owner elected (2026-05-23) to proceed on
> the supersession (Justyn) + de-minimis (mike) analyses in ADR-0017
> rather than send these emails. The templates are kept as a record
> of the path considered, not as a to-do.
>
> If a future contributor situation makes consent-outreach the right
> path (e.g. a substantive external contribution arrives), these
> templates are a starting point.

These are draft email templates the owner *would have sent* to the
two external contributors (`mike` and `Justyn Hunter`) before the
relicense PR ([ADR-0017](adr/0017-relicense-gpl-mit.md)) merges.

The goal of these emails would have been **consent for the relicense,
in writing** — a one-line reply is sufficient. The PR is structured
so that *neither* response (consent OR no response) blocks the
relicense; ADR-0017 documents the supersession (Justyn) and
de-minimis (mike) analyses that cover the no-consent case, and the
owner elected to proceed on those analyses without outreach.

---

## Template — `mike <mmccabe1993@gmail.com>`

**Subject:** WebReaper relicense (GPL-3.0 → MIT) — consent for your 4 commits?

Hi mike,

A quick courtesy email — I'm relicensing WebReaper from GPL-3.0 to
MIT, and your four commits from November 2025 (the .NET 10 upgrade
and the NuGet metadata fixes) are in the history.

The change is *more permissive*, not less: anyone who could use
WebReaper under GPL still can; people who couldn't (commercial
embedders, primarily) now can. Your contributions stay open-source
forever, just under more-permissive terms. Your authorship is
preserved in the git history and credited in CONTRIBUTORS.md.

Would you reply "I consent to relicensing my contributions to
WebReaper from GPL-3.0-or-later to MIT" (one line, this email
thread, no signature ceremony) by **[DATE — owner picks ~2 weeks
out]**?

If I don't hear back by then I'll proceed anyway — the analysis is
that your contributions (csproj metadata, framework version strings,
dependency bumps) are factual edits where the same change is the only
correct one, and so they're *de minimis* / clean-room-equivalent.
But your consent is the cleanest path and I'd rather have it.

Thanks for the contributions — the .NET 10 upgrade in particular was
useful.

— Alex

---

## Template — `Justyn Hunter <jhunter@gsandf.com>` / `<justynhunter@gmail.com>`

**Subject:** WebReaper relicense (GPL-3.0 → MIT) — consent for your 2 commits?

Hi Justyn,

A quick courtesy email — I'm relicensing WebReaper from GPL-3.0 to
MIT, and your two commits from November 2023 (the
`WithContentParser` plumbing and the whitespace undo) are in the
history.

A note for transparency: the code those commits added has actually
been replaced twice since:
- The `IContentParser` interface was removed in 6.0.0 when the parser
  pipeline moved off Newtonsoft to System.Text.Json.
- The `WithContentParser` method was renamed to `WithContentExtractor`
  in the recent ADR-0039 work.

So the original shape (the *function* of "register a custom parser")
survives, but the specific expression of it isn't in the current tree
anymore.

The relicense is *more permissive*, not less: anyone who could use
WebReaper under GPL still can; people who couldn't now can. Your
authorship is preserved in the git history and credited in
CONTRIBUTORS.md.

Would you reply "I consent to relicensing my contributions to
WebReaper from GPL-3.0-or-later to MIT" (one line, this email
thread, no signature ceremony) by **[DATE — owner picks ~2 weeks
out]**?

If I don't hear back by then I'll proceed anyway — given the
supersession, the legal analysis is that your specific expression
is no longer in the codebase. But your consent is the cleanest path
and I'd rather have it.

Thanks for the original work — it was the right shape at the time.

— Alex

---

## Tracking

| Contributor | Email sent | Outcome |
|---|---|---|
| mike | **No — owner elected to proceed on the de-minimis analysis** (2026-05-23) | Proceeded under analysis in ADR-0017 |
| Justyn Hunter | **No — owner elected to proceed on the supersession analysis** (2026-05-23) | Proceeded under analysis in ADR-0017 |

The supersession (Justyn) and de-minimis (mike) analyses in ADR-0017
were judged sufficient; the templates above are the record of the
alternative path that was considered.

## Gate 1 status: dissolved by history rewrite

Gate 1 in ADR-0017 was originally the Deloitte employer-IP check —
confirming with Deloitte that the project was personal work and not
employer-claimed. **This gate is now structurally dissolved.** The
`git filter-repo` rewrite (documented in ADR-0017's "History rewrite"
section) replaced the `olpavlov@deloitte.com` email with the owner's
personal `alexppavlov93@gmail.com` across all 41 affected commits.
The Deloitte identity no longer exists in the project's git history;
there is nothing for an employer-IP claim to point at.

The author NAMES on those commits (`Oleksandr Pavlov` / `Alexander
Pavlov`) were left intact — they are valid personal-name variants
the owner used at the time. The rewrite is email-only.

Old history is preserved on `origin/pre-deloitte-cleanup-master` and
`origin/pre-deloitte-cleanup-ai-native-wave` for ~30 days for
recoverability; after that those refs can be deleted.

## When Gate 3 clears

Gate 1 (Deloitte employer-IP check) dissolved by the email rewrite.
Gate 2 cleared on the supersession (Justyn) + de-minimis (mike)
analyses without outreach (2026-05-23). Only **Gate 3** remains:

1. Owner reviews the PR diff one final time.
2. Clicks merge.

The merge is the moment the project becomes MIT.
