import {
  Brain,
  Check,
  FileJson,
  Sparkles,
  Workflow,
  X,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { CodeBlock } from "@/components/code/code-block";
import { CopyButton } from "@/components/code/copy-button";
import { HeroClimb } from "@/components/playground/hero-climb";
import { Configurator } from "@/components/playground/configurator";
import { GitHubIcon } from "@/components/icons";
import { siteConfig } from "@/lib/site";

const container = "mx-auto max-w-7xl px-4 sm:px-6 lg:px-8";

/* ---- concept spine data ------------------------------------------------- */

const seams = [
  {
    role: "bg-role-transport",
    name: "Transport",
    iface: "IPageLoadTransport",
    body: "How a URL becomes a document. HTTP lives in core; browsers are satellite parts.",
    parts: ["HTTP (core)", "WebReaper.Playwright", "WebReaper.Cdp", "Stealth.CloakBrowser"],
  },
  {
    role: "bg-role-extract",
    name: "Extraction",
    iface: "IContentExtractor",
    body: "How a document becomes data. Deterministic fold by default; LLM adapters when you ask.",
    parts: ["Markdown (core)", "Schema fold (core)", "WebReaper.AI inferrer", "LLM fallback / self-heal"],
  },
  {
    role: "bg-role-sink",
    name: "Destination",
    iface: "IScraperSink",
    body: "Where records land. Fan-out is concurrent; add as many sinks as you like.",
    parts: ["Console / CSV / JSONL", "WebReaper.Mongo", "WebReaper.Redis", "WebReaper.Cosmos"],
  },
  {
    role: "bg-foreground",
    name: "Crawl state",
    iface: "IScheduler · IVisitedLinkTracker",
    body: "Queue and dedup live behind seams too: swap in Redis or Service Bus and many workers share one crawl. Same code, distributed.",
    parts: ["in-memory / file (core)", "WebReaper.Sqlite", "WebReaper.Redis", "AzureServiceBus"],
  },
];

const rungs = [
  { step: "1 · HTTP", note: "fast fetch, zero overhead", status: "403 challenge", tone: "text-red-600" },
  { step: "2 · Browser", note: "headless Chromium render", status: "still blocked", tone: "text-red-600" },
  { step: "3 · Stealth", note: "CloakBrowser fork", status: "200 OK", tone: "text-role-sink" },
];

const climbRules = [
  "Climbs only when a page actually looks blocked: status, header, or body marker.",
  "The first confirmed block lifts that host's floor, so the rest of the crawl starts at the working tier.",
  "Still blocked at the top? The page is dropped and the run exits non-zero. Challenge pages never masquerade as data.",
  "Self-hosted. No cloud round-trip. Stealth (~220 MB) downloads only if you opt in.",
];

const agents = [
  {
    title: "Claude Code skill",
    body: 'One command writes the skill; the next session routes "scrape X" intents to the CLI automatically.',
    code: `$ webreaper init
Wrote WebReaper Agent Skill to
  .claude/skills/webreaper/SKILL.md`,
  },
  {
    title: "MCP servers",
    body: "Six tools: scrape, map, extract, extract_with_prompt, extract_inferred, crawl. Over stdio (Cursor, Claude Desktop) or Streamable HTTP (n8n).",
    code: `$ docker run -p 8080:8080 \\
  -e WEBREAPER_MCP_TOKEN=secret \\
  ghcr.io/alex-on-ai/webreaper-mcp-http`,
  },
  {
    title: "Autonomous agent (library)",
    body: "A goal, a URL, any IChatClient. Decide, persist, execute, with durable resume across restarts.",
    code: `var r = await LlmAgent.RunAsync(
  "https://example.com",
  goal: "find the support email",
  chatClient);`,
  },
];

/* ---- folded-in webreaper.ai value: features, comparison, pricing, faq --- */

const features = [
  {
    eyebrow: "Markdown by default",
    title: "Any page to clean, LLM-ready Markdown",
    body: "No schema, no selectors. Point WebReaper at a URL and get back tidy Markdown you can pipe straight into a prompt or a vector store.",
    icon: Brain,
    lang: "csharp",
    code: `using WebReaper.Builders;

var engine = await ScraperEngineBuilder
    .Crawl("https://news.ycombinator.com")
    .AsMarkdown()
    .WriteToConsole()
    .BuildAsync();

await engine.RunAsync();`,
  },
  {
    eyebrow: "Typed extraction",
    title: "Structured data with compile-time schemas",
    body: "Declare fields once on a POCO. A Roslyn source generator emits a static schema and a reflection-free materializer that is AOT-clean, with no runtime guessing.",
    icon: FileJson,
    lang: "csharp",
    code: `[ScrapeSchema]
public partial class Article
{
    [ScrapeField("h1")] public string? Title { get; set; }
    [ScrapeField(".score", Type = SchemaFieldType.Integer)]
    public int Points { get; set; }
    [ScrapeField(".tag", IsList = true)]
    public List<string> Tags { get; set; } = new();
}

await ScraperEngineBuilder
    .Crawl("https://example.com/post")
    .Extract(Article.Schema)
    .BuildAsync();`,
  },
  {
    eyebrow: "Deterministic first, LLM as rescue",
    title: "Self-healing extraction that costs nothing when it works",
    body: "Cheap CSS selectors run first. If a field comes back empty, the LLM fills it and caches the fix. Stable pages cost zero LLM calls.",
    icon: Workflow,
    lang: "csharp",
    code: `using WebReaper.AI;

var engine = await ScraperEngineBuilder
    .Crawl("https://example.com")
    .Extract(Article.Schema)
    .WithLlmFallback(chatClient)   // OpenAI, Anthropic, Ollama…
    .WriteToJsonFile("articles.jsonl")
    .BuildAsync();`,
  },
];

const comparison = {
  columns: ["WebReaper", "Firecrawl", "Crawl4AI", "Crawlee"],
  rows: [
    { label: "Single self-contained binary", values: ["yes", "no", "no", "no"] },
    { label: "MIT licensed", values: ["yes", "no", "yes", "yes"] },
    { label: "LLM extraction + autonomous agent", values: ["yes", "yes", "partial", "no"] },
    { label: "Auto bot-check stealth", values: ["yes", "partial", "partial", "partial"] },
    { label: "Pluggable distributed backends", values: ["yes", "yes", "no", "yes"] },
    { label: "Runs natively in .NET / C#", values: ["yes", "no", "no", "no"] },
  ],
};

const pricing = [
  {
    name: "Open Source",
    price: "Free",
    tagline: "The library, CLI, and Claude Code skill. MIT, self-hosted, forever.",
    cta: "Install now",
    href: "/docs/getting-started",
    featured: false,
  },
  {
    name: "Cloud",
    price: "Early access",
    tagline: "Hosted scheduled crawls, managed proxies and stealth, a team dashboard.",
    cta: "Join the waitlist",
    href: "/pricing",
    featured: true,
  },
  {
    name: "Enterprise",
    price: "Custom",
    tagline: "SSO, SLAs, on-prem, private satellites, and dedicated support.",
    cta: "Contact sales",
    href: "/pricing",
    featured: false,
  },
];

const faqs = [
  {
    q: "Is WebReaper really free?",
    a: "Yes. The library, the CLI, and the Claude Code skill are MIT licensed and free forever. You only pay if you later choose the optional hosted Cloud or Enterprise tiers.",
  },
  {
    q: "Do I have to use an LLM?",
    a: "No. WebReaper is deterministic by default: CSS/XPath selectors and clean Markdown need no model. The AI features are opt-in and bring-your-own LLM, so you only pay for tokens when you ask for them.",
  },
  {
    q: "How is it different from Firecrawl?",
    a: "Firecrawl is a hosted, AGPL-licensed cloud service. WebReaper is a local-first, MIT-licensed binary and .NET library. You run it yourself, embed it in commercial code, and bring any LLM.",
  },
  {
    q: "Can it handle JavaScript and bot protection?",
    a: "Yes. Swap the HTTP transport for Playwright or raw CDP for JS rendering, and the CLI auto-climbs to a stealth Chromium backend on Cloudflare, DataDome, or PerimeterX challenges.",
  },
  {
    q: "Does it scale to large crawls?",
    a: "The crawl loop is parallel by design. Swap the scheduler, visited-link tracker, and result sink to Redis, MongoDB, SQLite, Azure Service Bus, or Cosmos and run many workers against shared state.",
  },
];

const installRows = [
  { label: "macOS / Linux", cmd: siteConfig.install.brew },
  { label: "any POSIX sh", cmd: siteConfig.install.curl },
  { label: ".NET library", cmd: siteConfig.install.nuget },
];

function Mark({ value }: { value: string }) {
  if (value === "yes")
    return (
      <>
        <Check className="mx-auto h-5 w-5 text-accent" aria-hidden />
        <span className="sr-only">Supported</span>
      </>
    );
  if (value === "no")
    return (
      <>
        <X className="mx-auto h-5 w-5 text-muted-2" aria-hidden />
        <span className="sr-only">Not supported</span>
      </>
    );
  return <span className="mx-auto block text-sm text-muted-2">Partial</span>;
}

export default function Home() {
  // The live Tier B climb is gated: it is metered (a real browser through a
  // residential proxy) and email-captured. Enable with
  // NEXT_PUBLIC_PLAYGROUND_TIERB_LIVE=1 once the Tier B Fly app + edge env are
  // set; until then the climb stays a recorded demo.
  const tierBLive = process.env.NEXT_PUBLIC_PLAYGROUND_TIERB_LIVE === "1";

  return (
    <>
      {/* ============================ HERO ============================ */}
      {/* Left-aligned; the configurator IS the hero. */}
      <section className="relative overflow-hidden border-b-2 border-border-strong">
        <div className="pointer-events-none absolute inset-0 -z-10 bg-dot opacity-70" />
        <div className={`${container} pb-16 pt-12 sm:pb-20 sm:pt-16`}>
          <div className="max-w-3xl">
            <div className="flex flex-wrap gap-2 text-xs font-semibold text-foreground">
              {[
                "MIT licensed",
                "~12 MB single binary",
                ".NET library",
                "MCP servers",
                "any LLM, or none",
              ].map((f) => (
                <span
                  key={f}
                  className="rounded-full border-2 border-border-strong bg-surface px-3 py-1"
                >
                  {f}
                </span>
              ))}
            </div>

            <h1 className="mt-5 text-[2.8rem] font-extrabold leading-[0.98] tracking-tight sm:text-[4.25rem]">
              <span className="text-foreground">Scrape any site.</span>
              <br />
              <span className="text-accent">Feed your AI.</span>
            </h1>

            <p className="mt-5 max-w-2xl text-lg leading-relaxed text-muted">
              WebReaper is an AI-native web scraper for .NET: one ~12 MB binary
              that turns any site, even bot-protected ones, into clean Markdown or
              structured data.{" "}
              <strong className="text-foreground">
                Build the line below and the command and the code write
                themselves.
              </strong>
            </p>

            <div className="mt-7 flex flex-wrap items-center gap-3">
              <Button href="/docs/getting-started" size="lg">
                Get started
              </Button>
              <Button href={siteConfig.links.github} variant="secondary" size="lg">
                <GitHubIcon className="h-4 w-4" />
                Star on GitHub
              </Button>
              <div className="flex items-center gap-2.5 rounded-lg border-2 border-border-strong bg-surface px-3.5 py-2.5 font-mono text-sm shadow-[3px_3px_0_var(--shadow-ink)]">
                <span className="select-none text-muted-2">$</span>
                <code className="text-foreground">{siteConfig.install.brew}</code>
                <CopyButton value={siteConfig.install.brew} className="shrink-0" />
              </div>
            </div>
          </div>

          {/* the configurator is the centrepiece, full width */}
          <div className="mt-10">
            <Configurator />
          </div>
        </div>
      </section>

      {/* ==================== EVERY STATION IS A SEAM ==================== */}
      <section className={`${container} py-20 sm:py-28`}>
        <div className="max-w-2xl">
          <h2 className="text-3xl font-extrabold tracking-tight sm:text-4xl">
            Every station is a seam.
          </h2>
          <p className="mt-4 text-lg leading-relaxed text-muted">
            The builder is sugar over public interfaces. Don&rsquo;t like a
            station? Implement one interface and clip your own part into the line:
            the other stations never notice. Fifteen NuGet packages of ready parts
            ship in lockstep.
          </p>
        </div>
        <div className="mt-12 grid gap-4 sm:grid-cols-2">
          {seams.map((seam) => (
            <div key={seam.name} className="surface-card p-6">
              <div className="flex items-center gap-2.5">
                <span
                  className={`h-3.5 w-3.5 rounded-sm border-2 border-border-strong ${seam.role}`}
                />
                <h3 className="text-lg font-bold tracking-tight">{seam.name}</h3>
              </div>
              <code className="mt-3 inline-block rounded-md border border-border bg-surface-2 px-2 py-0.5 font-mono text-[13px] text-foreground">
                {seam.iface}
              </code>
              <p className="mt-3 text-sm leading-relaxed text-muted">{seam.body}</p>
              <ul className="mt-4 flex flex-wrap gap-1.5">
                {seam.parts.map((part) => (
                  <li
                    key={part}
                    className="rounded-full border border-border bg-surface px-2.5 py-0.5 font-mono text-[11px] text-muted"
                  >
                    {part}
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>
      </section>

      {/* ==================== AI-NATIVE FEATURE BEATS ==================== */}
      <section className="border-y-2 border-border-strong bg-background-subtle/50">
        <div className={`${container} space-y-16 py-20 sm:py-28`}>
          <div className="max-w-2xl">
            <span className="inline-flex items-center gap-2 rounded-full border-2 border-border-strong bg-surface px-3 py-1 text-xs font-semibold text-accent">
              <Sparkles className="h-3.5 w-3.5" /> AI-native
            </span>
            <h2 className="mt-4 text-3xl font-extrabold tracking-tight sm:text-4xl">
              Deterministic where you can, AI where you must.
            </h2>
            <p className="mt-4 text-lg leading-relaxed text-muted">
              Start with fast, free selectors. Reach for an LLM only when the page
              fights back, and cache the fix so stable pages stay free.
            </p>
          </div>

          {features.map(({ eyebrow, title, body, icon: Icon, code, lang }, i) => (
            <div
              key={title}
              className="grid items-center gap-8 lg:grid-cols-2 lg:gap-12"
            >
              <div className={i % 2 === 1 ? "lg:order-2" : ""}>
                <div className="flex items-center gap-2 text-sm font-semibold text-accent">
                  <Icon className="h-4 w-4" />
                  {eyebrow}
                </div>
                <h3 className="mt-3 text-2xl font-bold tracking-tight">{title}</h3>
                <p className="mt-3 text-pretty leading-relaxed text-muted">{body}</p>
              </div>
              <div className={i % 2 === 1 ? "lg:order-1" : ""}>
                <CodeBlock code={code} lang={lang} filename="Program.cs" />
              </div>
            </div>
          ))}
        </div>
      </section>

      {/* ==================== THE LINE DEFENDS ITSELF ==================== */}
      <section className={`${container} py-20 sm:py-28`}>
        <div className="max-w-2xl">
          <h2 className="text-3xl font-extrabold tracking-tight sm:text-4xl">
            The line defends itself.
          </h2>
          <p className="mt-4 text-lg leading-relaxed text-muted">
            Most scrapers make you guess when a site is blocking you. WebReaper
            detects the block and escalates on its own, per page, paying the climb
            once per host.
          </p>
        </div>

        <div className="mt-12 grid gap-6 lg:grid-cols-2 lg:gap-10">
          <div className="surface-card p-6 sm:p-7">
            <div className="flex flex-col gap-3">
              {rungs.map((rung) => (
                <div
                  key={rung.step}
                  className="flex items-center gap-3 rounded-lg border border-border px-4 py-3"
                >
                  <b className="font-mono text-sm">{rung.step}</b>
                  <small className="text-muted-2">{rung.note}</small>
                  <span
                    className={`ml-auto rounded border border-current px-2 py-0.5 font-mono text-xs ${rung.tone}`}
                  >
                    {rung.status}
                  </span>
                </div>
              ))}
            </div>
            <ul className="mt-5 space-y-2.5 text-sm leading-relaxed text-muted">
              {climbRules.map((rule) => (
                <li key={rule} className="relative pl-5">
                  <span className="absolute left-0 font-bold text-role-transport">
                    &rarr;
                  </span>
                  {rule}
                </li>
              ))}
            </ul>
          </div>

          <div className="flex flex-col">
            <p className="mb-3 text-sm font-semibold text-muted">
              Watch a real run climb HTTP &rarr; browser &rarr; stealth on a
              blocked site.
            </p>
            <HeroClimb live={tierBLive} />
          </div>
        </div>
      </section>

      {/* ==================== CLIPS INTO YOUR AGENT ==================== */}
      <section className="border-y-2 border-border-strong bg-background-subtle/50">
        <div className={`${container} py-20 sm:py-28`}>
          <div className="max-w-2xl">
            <h2 className="text-3xl font-extrabold tracking-tight sm:text-4xl">
              Clips straight into your agent.
            </h2>
            <p className="mt-4 text-lg leading-relaxed text-muted">
              Three interop surfaces, one engine underneath.
            </p>
          </div>
          <div className="mt-12 grid gap-4 lg:grid-cols-3">
            {agents.map((agent) => (
              <div key={agent.title} className="surface-card flex flex-col p-6">
                <h3 className="text-lg font-bold tracking-tight">{agent.title}</h3>
                <p className="mt-2 flex-1 text-sm leading-relaxed text-muted">
                  {agent.body}
                </p>
                <pre className="mt-5 overflow-x-auto rounded-lg bg-foreground p-3.5 font-mono text-[12px] leading-relaxed text-[#e9e6f1]">
                  {agent.code}
                </pre>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* ==================== COMPARISON ==================== */}
      <section className={`${container} py-20 sm:py-28`}>
        <div className="max-w-2xl">
          <h2 className="text-3xl font-extrabold tracking-tight sm:text-4xl">
            How WebReaper compares.
          </h2>
          <p className="mt-4 text-lg leading-relaxed text-muted">
            Local-first and MIT licensed, with the AI features people reach for the
            cloud to get.
          </p>
        </div>
        <div className="mt-12 overflow-x-auto">
          <table className="w-full min-w-[640px] border-separate border-spacing-0 text-sm">
            <thead>
              <tr>
                <th className="w-2/5 px-4 py-3 text-left font-medium text-muted" />
                {comparison.columns.map((col, i) => (
                  <th
                    key={col}
                    className={`px-4 py-3 text-center font-bold ${
                      i === 0 ? "text-accent" : "text-muted"
                    }`}
                  >
                    {col}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {comparison.rows.map((row) => (
                <tr key={row.label} className="transition-colors hover:bg-surface-2/50">
                  <td className="border-t border-border px-4 py-3.5 text-left font-medium text-foreground">
                    {row.label}
                  </td>
                  {row.values.map((value, j) => (
                    <td
                      key={j}
                      className={`border-t border-border px-4 py-3.5 text-center ${
                        j === 0 ? "bg-accent/[0.05]" : ""
                      }`}
                    >
                      <Mark value={value} />
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      {/* ==================== PRICING TEASER ==================== */}
      <section className="border-y-2 border-border-strong bg-background-subtle/50">
        <div className={`${container} py-20 sm:py-28`}>
          <div className="max-w-2xl">
            <h2 className="text-3xl font-extrabold tracking-tight sm:text-4xl">
              Free to run. Pay only to scale.
            </h2>
            <p className="mt-4 text-lg leading-relaxed text-muted">
              The open-source core does everything locally. Hosted tiers add
              scheduling, managed infrastructure, and a team UI.
            </p>
          </div>
          <div className="mt-12 grid gap-4 lg:grid-cols-3">
            {pricing.map((tier) => (
              <div
                key={tier.name}
                className={`surface-card flex flex-col p-6 ${
                  tier.featured ? "ring-2 ring-accent" : ""
                }`}
              >
                {tier.featured ? (
                  <span className="mb-3 inline-flex w-fit rounded-full bg-accent/15 px-2.5 py-0.5 text-xs font-semibold text-accent">
                    Early access
                  </span>
                ) : null}
                <h3 className="text-lg font-bold">{tier.name}</h3>
                <p className="mt-2 text-2xl font-extrabold tracking-tight">
                  {tier.price}
                </p>
                <p className="mt-3 flex-1 text-sm leading-relaxed text-muted">
                  {tier.tagline}
                </p>
                <Button
                  href={tier.href}
                  variant={tier.featured ? "primary" : "secondary"}
                  className="mt-6 w-full"
                >
                  {tier.cta}
                </Button>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* ==================== FAQ ==================== */}
      <section className={`${container} py-20 sm:py-28`}>
        <div className="mx-auto max-w-3xl">
          <h2 className="text-3xl font-extrabold tracking-tight sm:text-4xl">
            Frequently asked questions
          </h2>
          <div className="mt-10 divide-y divide-border border-y-2 border-border-strong">
            {faqs.map((faq) => (
              <details key={faq.q} className="group py-5">
                <summary className="flex cursor-pointer list-none items-center justify-between gap-4 text-left font-semibold text-foreground">
                  {faq.q}
                  <span className="text-muted transition group-open:rotate-45">
                    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round"><path d="M12 5v14M5 12h14" /></svg>
                  </span>
                </summary>
                <p className="mt-3 text-pretty leading-relaxed text-muted">{faq.a}</p>
              </details>
            ))}
          </div>
        </div>
      </section>

      {/* ==================== BOLT IT TO YOUR BENCH (install finale) ===== */}
      <section className={`${container} pb-24`}>
        <div className="surface-card overflow-hidden border-2 border-border-strong bg-foreground p-8 sm:p-10">
          <h2 className="text-3xl font-extrabold tracking-tight text-white sm:text-4xl">
            Bolt it to your bench.
          </h2>
          <div className="mt-7 space-y-2.5">
            {installRows.map((row) => (
              <div
                key={row.label}
                className="flex flex-wrap items-center gap-3 rounded-lg border border-[#453f52] px-4 py-3"
              >
                <b className="w-28 shrink-0 text-sm font-medium text-[#a29bb3]">
                  {row.label}
                </b>
                <code className="min-w-0 flex-1 overflow-x-auto whitespace-nowrap font-mono text-sm text-[#e9e6f1] [scrollbar-width:none]">
                  {row.cmd}
                </code>
                <span className="shrink-0 text-[#e9e6f1]">
                  <CopyButton value={row.cmd} />
                </span>
              </div>
            ))}
          </div>
          <p className="mt-6 text-sm text-[#b7b1c6]">
            Windows binaries on{" "}
            <a
              href={`${siteConfig.links.github}/releases/latest`}
              target="_blank"
              rel="noreferrer noopener"
              className="font-medium text-white underline underline-offset-2"
            >
              GitHub Releases
            </a>{" "}
            · six platforms · macOS builds are Apple-notarized · no Docker, no
            signup, no metering.
          </p>
        </div>
      </section>
    </>
  );
}
