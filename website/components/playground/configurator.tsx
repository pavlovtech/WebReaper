"use client";

import { useMemo, useState } from "react";
import { CopyButton } from "@/components/code/copy-button";

/**
 * The Reaping Line configurator. Assemble a scrape from four stations and the
 * exact CLI command + equivalent C# builder chain write themselves. Every option
 * maps to a real flag or builder call (verified against the shipped binary's
 * help output); a whole-site crawl has no start-tier flag because the real CLI
 * auto-climbs per page (ADR-0083).
 */

type Scope = "page" | "site";
type Transport = "http" | "browser" | "stealth";
type Extract = "md" | "schema" | "infer" | "prompt";
type Sink = "stdout" | "file" | "mongo";

const URLS: Record<Scope, string> = {
  page: "https://example.com/post",
  site: "https://example.com",
};

function buildCli(s: {
  scope: Scope;
  transport: Transport;
  extract: Extract;
  sink: Sink;
}): string {
  const url = URLS[s.scope];
  const parts = ["webreaper", s.scope === "page" ? "scrape" : "crawl", url];
  // Start-tier flags exist only on `scrape`; a crawl auto-climbs per page.
  if (s.scope === "page" && s.transport === "browser") parts.push("--browser");
  if (s.scope === "page" && s.transport === "stealth") parts.push("--stealth");
  if (s.extract === "schema") parts.push("--schema", "schema.json");
  if (s.extract === "infer")
    parts.push(
      "--infer",
      '"title, author, date"',
      "--model",
      "gpt-4o-mini",
      "--llm-url",
      "https://api.openai.com/v1",
    );
  if (s.extract === "prompt")
    parts.push(
      "--prompt",
      '"title and author"',
      "--model",
      "gpt-4o-mini",
      "--llm-url",
      "https://api.openai.com/v1",
    );
  if (s.scope === "site") parts.push("--max-pages", "500");
  let cmd = parts.join(" ");
  if (s.sink === "file")
    cmd +=
      s.scope === "site"
        ? " --output-dir ./out"
        : " --output page" + (s.extract === "md" ? ".md" : ".json");
  if (s.sink === "stdout" && s.scope === "site") cmd += " > pages.jsonl";
  return cmd;
}

function cliTokens(cmd: string) {
  let inQuote = false;
  return cmd.split(" ").map((w, i) => {
    if (i === 0 || i === 1)
      return { t: w, c: "text-foreground font-semibold" };
    const opens = w.startsWith('"');
    const closes = w.endsWith('"');
    if (opens || inQuote) {
      if (opens && !closes) inQuote = true;
      if (closes) inQuote = false;
      return { t: w, c: "text-role-sink" };
    }
    if (w.startsWith("--")) return { t: w, c: "text-role-transport" };
    return { t: w, c: "" };
  });
}

function csLines(s: {
  scope: Scope;
  transport: Transport;
  extract: Extract;
  sink: Sink;
}): { t: string; c?: string }[] {
  const K = "text-role-extract"; // keyword
  const M = "text-foreground font-semibold"; // method
  const S = "text-role-sink"; // string
  const C = "text-muted-2"; // comment
  const L: { t: string; c?: string }[] = [];
  const browser = s.transport !== "http";
  L.push({ t: "using", c: K }, { t: " WebReaper.Builders;" });
  const usings: string[] = [];
  if (s.transport === "browser") usings.push("WebReaper.Playwright");
  if (s.transport === "stealth") usings.push("WebReaper.Stealth.CloakBrowser");
  if (s.extract === "infer" || s.extract === "prompt")
    usings.push("WebReaper.AI");
  if (s.sink === "mongo") usings.push("WebReaper.Mongo");
  for (const u of usings) L.push({ t: `\nusing ${u};` });
  L.push({ t: "\n\n" });
  L.push(
    { t: "await using var", c: K },
    { t: " engine = " },
    { t: "await", c: K },
    { t: " ScraperEngineBuilder" },
  );
  L.push({
    t: `\n    .${browser ? "CrawlWithBrowser" : "Crawl"}(`,
  });
  L.push({ t: `"${URLS[s.scope]}"`, c: S }, { t: ")" });
  if (s.extract === "md") L.push({ t: "\n    .AsMarkdown()", c: M });
  if (s.extract === "schema")
    L.push({
      t: '\n    .Extract(new()\n    {\n        new("title", "h1"),\n        new("author", ".byline a")\n    })',
    });
  if (s.extract === "infer") {
    L.push({ t: '\n    .ExtractInferred("title, author, date")' });
    L.push({ t: "\n    .WithLlmSchemaInferrer(chatClient)  " });
    L.push({ t: "// any IChatClient", c: C });
  }
  if (s.extract === "prompt")
    L.push({ t: '\n    .ExtractWithPrompt(chatClient, "title and author")' });
  if (s.scope === "site") {
    L.push({ t: "\n    .Sweep()                " });
    L.push({ t: "// recursive, on-domain", c: C });
    L.push({ t: "\n    .PageCrawlLimit(500)" });
  }
  if (s.transport === "browser")
    L.push({ t: "\n    .WithPlaywrightPageLoader()" });
  if (s.transport === "stealth") {
    L.push({ t: "\n    .WithCloakBrowser()      " });
    L.push({ t: "// auto-downloads on first use", c: C });
  }
  if (s.sink === "stdout") L.push({ t: "\n    .WriteToConsole()" });
  if (s.sink === "file")
    L.push({
      t: `\n    .WriteToJsonFile("${s.scope === "site" ? "pages.json" : "page.json"}")`,
    });
  if (s.sink === "mongo")
    L.push({ t: '\n    .WriteToMongoDb(conn, "scraper", "records")' });
  L.push({ t: "\n    .BuildAsync();" });
  L.push({ t: "\n\n" });
  L.push({ t: "await", c: K }, { t: " engine.RunAsync();" });
  return L;
}

function noteFor(s: {
  scope: Scope;
  transport: Transport;
  extract: Extract;
  sink: Sink;
}): React.ReactNode {
  if (s.scope === "site" && s.transport !== "http")
    return (
      <>
        <strong className="text-foreground">
          A crawl has no start-tier flag:
        </strong>{" "}
        every page starts at HTTP and auto-climbs on a block; the first confirmed
        block lifts that host&apos;s floor for the rest of the run (ADR-0083).
        {s.transport === "stealth" ? (
          <>
            {" "}
            Set <code>WEBREAPER_AUTO_STEALTH=1</code> to let the climb include
            the stealth rung unattended.
          </>
        ) : (
          <> The library can still pin a browser transport — see the C# pane.</>
        )}
      </>
    );
  if (s.scope === "page" && s.transport === "stealth")
    return (
      <>
        <strong className="text-foreground">--stealth</strong> starts the climb
        at the stealth rung, skipping the vanilla browser. The ~220 MB backend
        downloads only on opt-in.
      </>
    );
  if (s.scope === "page" && s.transport === "browser")
    return (
      <>
        <strong className="text-foreground">--browser</strong> starts the climb
        at the browser rung. Managed Chromium via{" "}
        <code>webreaper browser install</code>, or BYO with{" "}
        <code>--browser-cdp-url</code>.
      </>
    );
  if (s.transport === "http")
    return (
      <>
        <strong className="text-foreground">Auto-climb is on by default:</strong>{" "}
        a page that looks blocked escalates HTTP → browser on its own, no flag.
      </>
    );
  return null;
}

function extraNote(s: { extract: Extract; sink: Sink }): React.ReactNode {
  if (s.extract === "infer")
    return (
      <>
        <strong className="text-foreground">~1 LLM call per site:</strong> the
        schema is inferred once, then every page extracts deterministically. Key
        via <code>WEBREAPER_LLM_API_KEY</code>, never a flag.
      </>
    );
  if (s.extract === "prompt")
    return (
      <>
        <strong className="text-foreground">LLM on every page.</strong> On a
        whole-site crawl the CLI asks before a large run (<code>--yes</code>{" "}
        skips).
      </>
    );
  if (s.sink === "mongo")
    return (
      <>
        <strong className="text-foreground">MongoDB is a library sink</strong>{" "}
        (<code>WebReaper.Mongo</code>); the CLI pane shows the closest CLI
        equivalent — stdout you can pipe.
      </>
    );
  if (s.extract === "md")
    return (
      <>
        Markdown is the default — no schema, no LLM, no cost. The envelope is{" "}
        <code>{"{title, markdown, url}"}</code> per page.
      </>
    );
  return null;
}

const STATIONS = [
  {
    key: "scope" as const,
    label: "SCOPE",
    color: "var(--foreground)",
    opts: [
      { v: "page", name: "One page", sub: "webreaper scrape" },
      { v: "site", name: "Whole site", sub: "webreaper crawl → JSON Lines" },
    ],
  },
  {
    key: "transport" as const,
    label: "TRANSPORT",
    color: "var(--role-transport)",
    opts: [
      { v: "http", name: "HTTP", sub: "fast default; auto-climbs on block" },
      { v: "browser", name: "Browser", sub: "JS-rendered pages (Chromium)" },
      { v: "stealth", name: "Stealth", sub: "start at the CloakBrowser tier" },
    ],
  },
  {
    key: "extract" as const,
    label: "EXTRACTION",
    color: "var(--role-extract)",
    opts: [
      { v: "md", name: "Markdown", sub: "LLM-ready, no schema, $0" },
      { v: "schema", name: "CSS schema", sub: "deterministic fields, $0" },
      { v: "infer", name: "Inferred schema", sub: "LLM once per site", llm: "LLM ×1" },
      { v: "prompt", name: "Prompt", sub: "LLM on every page", llm: "LLM ×n" },
    ],
  },
  {
    key: "sink" as const,
    label: "DESTINATION",
    color: "var(--role-sink)",
    opts: [
      { v: "stdout", name: "stdout", sub: "pipe it anywhere" },
      { v: "file", name: "File", sub: "--output / shell redirect" },
      { v: "mongo", name: "MongoDB", sub: "library sink (satellite pkg)" },
    ],
  },
];

const NODES: [string, number, string][] = [
  ["SOURCE", 8, "var(--foreground)"],
  ["TRANSPORT", 33, "var(--role-transport)"],
  ["EXTRACT", 58, "var(--role-extract)"],
  ["SINK", 86, "var(--role-sink)"],
];

export function Configurator({ className = "" }: { className?: string }) {
  const [scope, setScope] = useState<Scope>("page");
  const [transport, setTransport] = useState<Transport>("http");
  const [extract, setExtract] = useState<Extract>("md");
  const [sink, setSink] = useState<Sink>("stdout");
  const state = { scope, transport, extract, sink };
  const setters: Record<string, (v: string) => void> = {
    scope: (v) => setScope(v as Scope),
    transport: (v) => setTransport(v as Transport),
    extract: (v) => setExtract(v as Extract),
    sink: (v) => setSink(v as Sink),
  };

  const cli = useMemo(() => buildCli(state), [scope, transport, extract, sink]);
  const cliMongo = sink === "mongo" ? buildCli({ ...state, sink: "stdout" }) : cli;
  const cs = useMemo(() => csLines(state), [scope, transport, extract, sink]);
  const csText = cs.map((l) => l.t).join("");
  const shape =
    extract === "md"
      ? scope === "site"
        ? "{title, markdown, url} × n pages (JSON Lines)"
        : "{title, markdown, url}"
      : scope === "site"
        ? "{your fields…} × n pages (JSON Lines)"
        : "{your fields…} (JSON)";

  return (
    <div
      className={`surface-card overflow-hidden !shadow-[6px_6px_0_var(--shadow-ink)] ${className}`}
    >
      {/* rig head */}
      <div className="flex items-center gap-3 border-b-2 border-border-strong bg-surface-2 px-4 py-2.5">
        <span className="flex gap-1.5" aria-hidden>
          {[0, 1, 2].map((i) => (
            <i
              key={i}
              className="h-2.5 w-2.5 rounded-full border-[1.5px] border-border-strong bg-surface"
            />
          ))}
        </span>
        <strong className="text-[13px] tracking-wide">
          REAPING LINE — configurator
        </strong>
        <span className="ml-auto hidden text-xs text-muted-2 sm:inline">
          every option is a real flag or builder call
        </span>
      </div>

      {/* stations */}
      <fieldset className="grid grid-cols-2 border-b-2 border-border-strong lg:grid-cols-4">
        <legend className="sr-only">Configure the scrape</legend>
        {STATIONS.map((st, si) => (
          <div
            key={st.key}
            className="border-border-soft border-b-2 border-dashed p-4 last:border-b-0 lg:border-b-0 lg:[&:not(:last-child)]:border-r-2"
            style={{ borderColor: undefined }}
          >
            <div className="mb-2.5 flex items-center gap-2 text-[13px] font-bold">
              <span
                className="h-3 w-3 flex-none rounded-[3px] border-[1.5px] border-border-strong"
                style={{ background: st.color }}
              />
              {st.label}
            </div>
            <div className="flex flex-col gap-1.5">
              {st.opts.map((o) => {
                const active =
                  state[st.key as keyof typeof state] === o.v;
                return (
                  <label
                    key={o.v}
                    className={`flex cursor-pointer items-center gap-2.5 rounded-lg border-[1.5px] px-2.5 py-1.5 text-sm font-semibold transition ${
                      active
                        ? "border-border-strong bg-surface-2 shadow-[2px_2px_0_var(--shadow-ink)]"
                        : "border-border hover:border-border-strong"
                    }`}
                  >
                    <input
                      type="radio"
                      name={st.key}
                      value={o.v}
                      checked={active}
                      onChange={() => setters[st.key](o.v)}
                      className="h-3.5 w-3.5 flex-none accent-accent"
                    />
                    <span className="min-w-0">
                      {o.name}
                      <small className="block text-xs font-medium leading-tight text-muted-2">
                        {o.sub}
                      </small>
                    </span>
                    {"llm" in o && o.llm ? (
                      <span className="ml-auto flex-none rounded border border-role-extract px-1 text-[10px] font-bold tracking-wide text-role-extract">
                        {o.llm}
                      </span>
                    ) : null}
                  </label>
                );
              })}
            </div>
          </div>
        ))}
      </fieldset>

      {/* belt */}
      <div
        className="relative h-[92px] border-b-2 border-border-strong"
        aria-hidden
      >
        <svg
          viewBox="0 0 1140 92"
          preserveAspectRatio="none"
          className="absolute inset-0 h-full w-full"
        >
          <line
            x1="0"
            y1="42"
            x2="1140"
            y2="42"
            stroke="var(--border)"
            strokeWidth="2"
          />
          {NODES.map(([n, x, c]) => (
            <g key={n}>
              <circle cx={x * 11.4} cy="42" r="9" fill={c} opacity="0.16" />
              <circle
                cx={x * 11.4}
                cy="42"
                r="6"
                fill="var(--surface)"
                stroke={c}
                strokeWidth="2.5"
              />
              <text
                x={x * 11.4}
                y="74"
                textAnchor="middle"
                className="fill-muted-2"
                style={{ font: '600 11px var(--font-mono)', letterSpacing: ".04em" }}
              >
                {n}
              </text>
            </g>
          ))}
        </svg>
      </div>

      {/* panes */}
      <div className="grid lg:grid-cols-[1.2fr_1fr]">
        <div className="flex min-w-0 flex-col">
          <div className="flex items-center gap-2 border-b border-border px-3.5 py-2 text-[12px] font-bold tracking-wide">
            CLI{" "}
            <span className="font-normal text-muted-2">one binary, no runtime</span>
            <CopyButton value={cliMongo} className="ml-auto" />
          </div>
          <pre className="flex-1 overflow-x-auto whitespace-pre px-4 py-4 font-mono text-[13px] leading-relaxed">
            {cliTokens(cliMongo).map((tk, i) => (
              <span key={i} className={tk.c}>
                {i > 0 ? " " : ""}
                {tk.t}
              </span>
            ))}
          </pre>
          <div className="border-t border-border px-3.5 py-2.5 text-xs leading-relaxed text-muted">
            {noteFor(state)}
          </div>
        </div>
        <div className="flex min-w-0 flex-col border-t-2 border-border-strong lg:border-l-2 lg:border-t-0">
          <div className="flex items-center gap-2 border-b border-border px-3.5 py-2 text-[12px] font-bold tracking-wide">
            C# — same line, as a library
            <CopyButton value={csText} className="ml-auto" />
          </div>
          <pre className="flex-1 overflow-x-auto whitespace-pre px-4 py-4 font-mono text-[13px] leading-relaxed">
            {cs.map((l, i) => (
              <span key={i} className={l.c}>
                {l.t}
              </span>
            ))}
          </pre>
          <div className="border-t border-border px-3.5 py-2.5 text-xs text-muted">
            Output shape: <code className="text-foreground">{shape}</code>
            <div className="mt-1 text-muted-2">{extraNote(state)}</div>
          </div>
        </div>
      </div>
    </div>
  );
}
