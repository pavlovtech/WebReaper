# Crawl outcome is a closed sum; no per-step strategy layer

The crawl step — the pure decision mapping a `Job` + its loaded document + the
parsing `Schema` to its result — returns a **closed** discriminated union
`CrawlOutcome` (`Parsed` | `Followed` | `Paginated`). A full alternative design
with an open, priority-ordered `ICrawlStepStrategy` extension layer was produced
and deliberately rejected.

Why closed: across every Example and integration test the selector chain is 1–3
deep with at most one trailing pagination step; there is no fourth page category
and no pluggable per-step behaviour anywhere in the codebase. A strategy
framework would be a seam with one adapter — indirection without variation — and
would push extension surface onto the one common caller (the Spider shell). If a
fourth page category ever appears, adding one arm to the closed union plus one
shell branch is a small, local change; that future cost is cheaper than carrying
the framework now.

The advance/retain selector-chain asymmetry (see CONTEXT.md "Flagged
ambiguities") is encoded structurally: `Followed.Next` and `Paginated.Items`
carry the **advanced** (dequeued) chain; `Paginated.NextPages` carries the
**retained** (original) chain. The historical implicit bug — two look-alike
`CreateNextJobs` call sites differing only in which chain they passed — becomes
unrepresentable.

## Considered options

- **Closed 3-arm sum (chosen).** The page category is the discriminator;
  advance vs retain are distinct named fields. Smallest surface that turns the
  asymmetry into a type.
- **Open strategy chain (rejected).** `ICrawlStepStrategy` + `Priority` +
  `ICrawlStepServices`. Extensible without modification, but a single-adapter
  seam the verified usage never exercises; cost lands on the common caller.
- **Minimal 2-arm sum (partially adopted).** One method, `Parsed | NextJobs`
  with advance/retain hidden inside the produced Jobs. Its purity argument was
  kept (the step takes no `CancellationToken`); hiding the asymmetry was not.
