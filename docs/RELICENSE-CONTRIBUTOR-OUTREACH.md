# Contributor outreach templates — GPL → MIT relicense (ADR-0017)

These are draft email templates the owner sends to the two external
contributors (`mike` and `Justyn Hunter`) before the relicense PR
([ADR-0017](adr/0017-relicense-gpl-mit.md)) merges. Personalise the
opening line and the timeline; the legal substance can stay as-is.

The goal of these emails is **consent for the relicense, in writing**
— a one-line reply is sufficient. The PR is structured so that
*neither* response (consent OR no response within ~2 weeks) blocks
the relicense; ADR-0017 documents the supersession (Justyn) and
de-minimis (mike) analyses that cover the no-response case. Consent
is preferred as the cleanest path.

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

| Contributor | Email sent | Response | Response date | Outcome |
|---|---|---|---|---|
| mike | _pending_ | — | — | _pending_ |
| Justyn Hunter | _pending_ | — | — | _pending_ |

Fill in as the outreach progresses. The PR description references
this table.

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

## When the two remaining gates clear

The owner:
1. Updates the tracking table here with the responses received from
   `mike` and `Justyn Hunter`.
2. Updates ADR-0017's Gate 2 + Gate 3 checkboxes (this file →
   that ADR).
3. Reviews the PR diff one final time.
4. Clicks merge.

The merge is the moment the project becomes MIT.
