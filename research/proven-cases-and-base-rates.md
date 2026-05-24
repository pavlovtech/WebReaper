# Proven cases & base rates — what actually works in web scraping

> **Date:** 2026-05-17 · **Status:** Evidence base for REPOSITIONING-PLAN.md Rev 3
> **Method:** three independent web-research tracks (commercial companies; OSS→revenue mechanics; solo/.NET open-core base rates). All figures are third-party aggregator estimates unless a primary source is cited; flagged inline. This file supersedes the speculative thesis in the other `research/` reports where they conflict — those are now hypotheses; this is the evidence.

## The question

Instead of inventing a novel strategy, follow what is *already proven* to produce durable revenue for a (solo/tiny-team) web-scraping effort — especially in .NET.

## Track 1 — Commercial scraping companies: ranked proven models

| Rank | Model | Evidence | Solo-reachable? | Proof |
|---|---|---|---|---|
| 1 | Proxy network + unblocking/API/dataset layers | Very strong | **No** (capital + legal-heavy) | Bright Data ~$300M ARR (PE-owned); Oxylabs ~$44M (conglomerate, no VC) |
| 2 | **Focused, usage-priced unblocking *API*, dev-sold via technical content** | **Strong** | **Yes** | **ScrapingBee** (2 founders, bootstrap, ~$1K→$8K MRR→$1M ARR in ~15mo→$5M ARR→8-fig exit); **ScraperAPI** (solo, $0 raised, $400K MRR→acquired & grew) |
| 3 | Hosted platform / scraper marketplace + OSS funnel | Moderate–strong | Hard solo | Apify (€6.7M, profitable, Crawlee→platform); Zyte (Scrapy→Zyte API+services, ~$20M) |
| 4 | AI-era LLM-ready extraction / browser-infra API | Emerging, momentum strong, durability unproven | VC-fuelled at frontier | Firecrawl ($16.2M raised, 350k devs, "profitable" per founder); Browserbase ($67.5M raised) |
| 5 | Automatic ML extraction + proprietary data product | Durable but small | Slow solo | Diffbot (~15 yrs profitable, only ~$3M rev) |

**Anti-patterns (consistently failed):** no-code/visual scrapers (import.io: $38M raised → absorbed; ParseHub shrinking; Octoparse capped ~$5.7M); VC-heavy horizontal "web data platform" with no wedge; pure OSS with no paid operational layer; undercapitalised proxy entry.

**Solo conclusion:** the *only* repeated solo/2-person real-revenue model in scraping is **#2 — the developer-sold, usage-priced unblocking API grown through deep technical content** (ScrapingBee, ScraperAPI). Both bootstrapped, both exited.

## Track 2 — OSS → revenue mechanics

- **Revenue tracks a billable hosted/managed operation, never GitHub stars.** Counter-evidence: Crawl4AI (~65k stars, solo) = sponsorship only, no revenue; Colly (~25k) = ~nothing; nodriver (industry-critical anti-bot infra) = ~nothing; ScrapeGraphAI (~25k stars) ≈ $330K (flagged).
- **The open-core line is universal:** the *library/logic* is free (permissive: BSD/Apache/MIT — none use copyleft to force payment); the *operation* is paid — hosted runtime, managed anti-ban/proxy, scale/reliability/SLA, marketplace. "The library is free; getting un-banned and not running the infra yourself is paid."
- **Durable money structurally needs a company** (Zyte, Apify, Firecrawl, Browserbase): SLA infra, proxy pools, billing, abuse/compliance, on-call. Sponsorship/patronage is universally insufficient as income even at 25k–65k stars.

## Track 3 — Solo / .NET open-core base rates (the decisive track)

| Rank | Playbook | .NET evidence | Solo verdict |
|---|---|---|---|
| 1 | **Revenue-threshold dual license** (free <~$1M/OSS/non-profit; paid commercial license; non-blocking key → enforced) | QuestPDF, ImageSharp/SLSL, MediatR/AutoMapper, MassTransit — the 2022–25 .NET consensus | Highest-probability **for stable libs**; weakest for scrapers |
| 2 | Open-core: free engine + paid Pro extensions/storage/support | Hangfire (solo, durable, $500–4,500/yr) | Durable; needs a feature seam buyers pay for |
| 3 | **Adjacent-paid-sibling funnel** — popular free lib as SEO/credibility funnel; sell a *separate stable* paid product | **ZZZ Projects** (Jonathan Magnan): runs **HtmlAgilityPack free** as funnel, ~$3M on separate bulk-ORM paid libs | **Highest ceiling; closest analog to WebReaper** |
| 4 | Services attached to OSS (consulting/support) | JasperFx ("just well enough to be encouraging"); pure time-for-money | Income floor, not scalable solo |
| 5 | Donations / GitHub Sponsors | QuestPDF: ~3% of income; FusionCache: 72M downloads, $0 by choice; ImageSharp: "<few months income over years" | **Proven to fail as income** |

**The exact-analog cautionary tale:** **AbotX** — a paid commercial .NET *crawler* (the open-core "Pro" model). It did not sustain, **reverted to free, project dormant.** Every solo .NET scraper that stayed a monetised library died (ScrapySharp), stalled (DotnetSpider, Fizzler), or did the Pro-tier bet and failed (AbotX).

**Why scrapers are the weakest dual-license category:** the salable thing isn't the parser (free primitives: AngleSharp/HtmlAgilityPack/Playwright); it's a moving anti-bot treadmill — endless unpaid maintenance, legally awkward to sell. Unenforced compliance is very low anyway (ImageSharp had to add build-time key enforcement).

## Decisive synthesis — the proven direction

Intersect all three tracks and one configuration dominates with positive precedent on every axis:

> **The library/CLI/skill stays free + permissive — it is the *funnel*, not the product. The monetised product is a separate, bootstrapped, usage-priced hosted unblocking/extraction *API* (ScrapingBee/ScraperAPI shape), with the free .NET library + CLI + agent skill + deep technical content as its credibility engine and on-ramp.**

- Proven *in scraping*: ScrapingBee, ScraperAPI (solo/2-person, bootstrapped, exited).
- Proven *for solo .NET*: ZZZ Projects (free HtmlAgilityPack funnel → separate paid product).
- Aligned with *where scraping money lives*: billable managed operation, not shipped library code.

**Explicitly rejected by the evidence:** monetising the library itself / a `WebReaper.Pro.*` commercial tier (the AbotX failure); donations as income; no-code; competing on raw proxies; assuming VC scale.

**Realistic revenue shape (ScrapingBee actual):** ~$1K MRR → ~$8K MRR → ~$1M ARR over ~15 months → $5M ARR → 8-figure exit. Bootstrapped, content-led, no VC.

**The #1 structural risk going in:** the durable money is a hosted operation, which historically needs company-scale ops (on-call, billing, abuse, reliability). The solo-tractable entry is precisely the ScrapingBee/ScraperAPI bootstrapped path; scaling it becomes a small company.

## Key sources

Commercial: Bright Data ([Wikipedia](https://en.wikipedia.org/wiki/Bright_Data), [GetLatka](https://getlatka.com/companies/brdta.com)); ScrapingBee ([Startups For the Rest of Us ep.783](https://www.startupsfortherestofus.com/episodes/episode-783-bootstrapping-scrapingbee-to-5m-arr-and-an-8-figure-exit), [$1M ARR post](https://www.scrapingbee.com/journey-to-one-million-arr/)); ScraperAPI ([Failory](https://www.failory.com/interview/scraper-api), [SaaS.group acquisition](https://www.prnewswire.com/news-releases/fe-international-advises-scraperapi-in-acquisition-by-saasgroup-301111971.html)); import.io ([Tracxn](https://tracxn.com/d/companies/import.io/__yFdBLWivi_X1O9CaDcue6pjzlTT1aMae3eGAk4U4iB4)).
OSS→revenue: Zyte ([history](https://www.zyte.com/blog/history-of-zyte-formerly-scrapinghub/), [pricing](https://www.zyte.com/pricing/)); Apify ([Crawlee announce](https://blog.apify.com/announcing-crawlee-the-web-scraping-and-browser-automation-library/), [rev-share](https://apify.com/partners/actor-developers)); Firecrawl ([TechCrunch](https://techcrunch.com/2025/08/19/ai-crawler-firecrawl-raises-14-5m-is-still-looking-to-hire-agents-as-employees/)); Crawl4AI ([GitHub](https://github.com/unclecode/crawl4ai), [MISSION.md](https://github.com/unclecode/crawl4ai/blob/main/MISSION.md)); Colly ([GitHub](https://github.com/gocolly/colly)).
Solo/.NET: ZZZ Projects ([mission](https://zzzprojects.com/mission), [HtmlAgilityPack](https://html-agility-pack.net/)); Hangfire ([pricing](https://www.hangfire.io/pricing/)); ImageSharp/SixLabors ([license change](https://sixlabors.com/posts/license-changes/), [enforcement](https://sixlabors.com/posts/licence-enforcement-changes/)); MediatR/AutoMapper ([Bogard](https://www.jimmybogard.com/automapper-and-mediatr-going-commercial/)); MassTransit ([v9](https://masstransit.io/introduction/v9-announcement)); QuestPDF ([discussion #491](https://github.com/QuestPDF/QuestPDF/discussions/491), [license](https://www.questpdf.com/license/)); FusionCache ([GitHub](https://github.com/ZiggyCreatures/FusionCache)); AbotX ([repo](https://github.com/sjdirect/abotx), [pricing](https://abotx.org/Buy/Pricing)); dead scrapers ([ScrapySharp](https://github.com/rflechner/ScrapySharp), [DotnetSpider](https://github.com/dotnetcore/DotnetSpider)).
