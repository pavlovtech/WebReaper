"use client";

import { useState } from "react";
import { Terminal } from "./terminal";
import { cn } from "@/lib/utils";

type Tab = "cli" | "library";

const cliLines = [
  { text: "# Hacker News", className: "font-semibold text-zinc-100" },
  { text: "" },
  { text: "- Show HN: An AI-native web scraper in .NET" },
  { text: "- Ask HN: Best way to feed a docs site into an LLM?" },
  { text: "- Launch HN: WebReaper, a single-binary crawler" },
  { text: "- Scraping behind Cloudflare without the headache" },
];

// Pre-tokenized C# so the snippet looks like an editor without a runtime
// highlighter on the client. Colors map to the same palette Shiki uses.
const csharp: { t: string; c?: string }[][] = [
  [{ t: "using", c: "kw" }, { t: " WebReaper.Builders;" }],
  [],
  [{ t: "var", c: "kw" }, { t: " engine = " }, { t: "await", c: "kw" }, { t: " ScraperEngineBuilder" }],
  [{ t: "    .", c: "pn" }, { t: "Crawl", c: "fn" }, { t: '("https://news.ycombinator.com")', c: "str" }],
  [{ t: "    .", c: "pn" }, { t: "AsMarkdown", c: "fn" }, { t: "()" }],
  [{ t: "    .", c: "pn" }, { t: "WriteToConsole", c: "fn" }, { t: "()" }],
  [{ t: "    .", c: "pn" }, { t: "BuildAsync", c: "fn" }, { t: "();" }],
  [],
  [{ t: "await", c: "kw" }, { t: " engine." }, { t: "RunAsync", c: "fn" }, { t: "();" }],
];

const tokenColor: Record<string, string> = {
  kw: "text-[#ff7b72]",
  fn: "text-[#d2a8ff]",
  str: "text-[#a5d6ff]",
  pn: "text-zinc-500",
};

export function HeroShowcase({ className }: { className?: string }) {
  const [tab, setTab] = useState<Tab>("cli");

  return (
    <div className={className}>
      <div
        className="mx-auto mb-3 flex w-fit items-center gap-1 rounded-lg border border-border bg-surface/60 p-1 backdrop-blur"
        role="tablist"
        aria-label="Choose CLI or library example"
      >
        {(
          [
            ["cli", "CLI"],
            ["library", "C# library"],
          ] as const
        ).map(([key, label]) => (
          <button
            key={key}
            type="button"
            role="tab"
            aria-selected={tab === key}
            onClick={() => setTab(key)}
            className={cn(
              "rounded-md px-3.5 py-1.5 text-sm font-medium transition",
              tab === key
                ? "bg-accent/15 text-accent"
                : "text-muted hover:text-foreground",
            )}
          >
            {label}
          </button>
        ))}
      </div>

      {tab === "cli" ? (
        <Terminal
          command="webreaper scrape https://news.ycombinator.com"
          lines={cliLines}
        />
      ) : (
        <div className="overflow-hidden rounded-xl border border-white/10 bg-[#0a0e13] shadow-2xl shadow-black/50">
          <div className="flex items-center gap-2 border-b border-white/10 px-4 py-3">
            <span className="flex gap-1.5">
              <span className="h-3 w-3 rounded-full bg-[#ff5f57]" />
              <span className="h-3 w-3 rounded-full bg-[#febc2e]" />
              <span className="h-3 w-3 rounded-full bg-[#28c840]" />
            </span>
            <span className="mx-auto font-mono text-xs text-zinc-500">
              Program.cs
            </span>
          </div>
          <pre className="overflow-x-auto p-4 font-mono text-[13px] leading-relaxed sm:p-5">
            <code>
              {csharp.map((line, i) => (
                <span key={i} className="block min-h-[1.25em]">
                  {line.map((tok, j) => (
                    <span key={j} className={tok.c ? tokenColor[tok.c] : "text-zinc-300"}>
                      {tok.t}
                    </span>
                  ))}
                </span>
              ))}
            </code>
          </pre>
        </div>
      )}
    </div>
  );
}
