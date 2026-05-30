"use client";

import { motion } from "motion/react";
import { cn } from "@/lib/utils";

type Line = { text: string; className?: string };

const defaultLines: Line[] = [
  { text: "# Hacker News", className: "font-semibold text-zinc-100" },
  { text: "" },
  { text: "- Show HN: An AI-native web scraper in .NET" },
  { text: "- Ask HN: Best way to feed a docs site into an LLM?" },
  { text: "- Launch HN: WebReaper, a single-binary crawler" },
  { text: "- Scraping behind Cloudflare without the headache" },
];

export function Terminal({
  className,
  command = "webreaper scrape https://news.ycombinator.com",
  title = "zsh",
  lines = defaultLines,
}: {
  className?: string;
  command?: string;
  title?: string;
  lines?: Line[];
}) {
  return (
    <div
      className={cn(
        "overflow-hidden rounded-xl border border-white/10 bg-[#0a0e13] shadow-2xl shadow-black/50",
        className,
      )}
    >
      <div className="flex items-center border-b border-white/10 px-4 py-3">
        <span className="flex gap-1.5">
          <span className="h-3 w-3 rounded-full bg-[#ff5f57]" />
          <span className="h-3 w-3 rounded-full bg-[#febc2e]" />
          <span className="h-3 w-3 rounded-full bg-[#28c840]" />
        </span>
        <span className="mx-auto font-mono text-xs text-zinc-500">{title}</span>
      </div>
      <div className="p-4 font-mono text-[13px] leading-relaxed sm:p-5">
        <div className="flex items-center gap-2">
          <span className="text-accent">❯</span>
          <span className="text-zinc-200">{command}</span>
        </div>
        <div className="space-y-1 pt-3">
          {lines.map((line, i) => (
            <motion.div
              key={i}
              initial={{ opacity: 0, y: 4 }}
              whileInView={{ opacity: 1, y: 0 }}
              viewport={{ once: true, margin: "-40px" }}
              transition={{ delay: 0.25 + i * 0.12, duration: 0.3 }}
              className={cn(
                "min-h-[1.25em] whitespace-pre-wrap",
                line.className ?? "text-zinc-400",
              )}
            >
              {line.text}
            </motion.div>
          ))}
          <motion.span
            aria-hidden
            initial={{ opacity: 0 }}
            animate={{ opacity: [0, 1, 1, 0] }}
            transition={{ repeat: Infinity, duration: 1.15, delay: 1.1 }}
            className="mt-1 inline-block h-4 w-2 translate-y-0.5 bg-accent/80"
          />
        </div>
      </div>
    </div>
  );
}
