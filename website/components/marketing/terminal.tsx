"use client";

import { useEffect, useRef, useState } from "react";
import { cn } from "@/lib/utils";

type Line = { text: string; className?: string };

const defaultCommand = "webreaper scrape https://news.ycombinator.com";
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
  command = defaultCommand,
  title = "zsh",
  lines = defaultLines,
}: {
  className?: string;
  command?: string;
  title?: string;
  lines?: Line[];
}) {
  const [typed, setTyped] = useState("");
  const [revealed, setRevealed] = useState(0);
  const [done, setDone] = useState(false);
  const started = useRef(false);

  useEffect(() => {
    if (started.current) return;
    started.current = true;

    const timers: ReturnType<typeof setTimeout>[] = [];
    const reduce = window.matchMedia(
      "(prefers-reduced-motion: reduce)",
    ).matches;
    if (reduce) {
      // Defer to a callback so we never call setState synchronously in the
      // effect body; reduced-motion users see the finished state immediately.
      timers.push(
        setTimeout(() => {
          setTyped(command);
          setRevealed(lines.length);
          setDone(true);
        }, 0),
      );
      return () => timers.forEach(clearTimeout);
    }

    let i = 0;
    const typeNext = () => {
      i += 1;
      setTyped(command.slice(0, i));
      if (i < command.length) {
        timers.push(setTimeout(typeNext, 34));
      } else {
        lines.forEach((_, idx) => {
          timers.push(
            setTimeout(() => setRevealed(idx + 1), 220 + idx * 120),
          );
        });
        timers.push(
          setTimeout(() => setDone(true), 220 + lines.length * 120),
        );
      }
    };
    timers.push(setTimeout(typeNext, 450));
    return () => timers.forEach(clearTimeout);
  }, [command, lines]);

  const typing = typed.length < command.length;

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
          <span className="text-zinc-200">
            {typed}
            {typing ? (
              <span className="ml-0.5 inline-block h-4 w-2 translate-y-0.5 animate-pulse bg-accent/80 align-middle" />
            ) : null}
          </span>
        </div>
        <div className="space-y-1 pt-3" aria-live="polite">
          {lines.map((line, i) => (
            <div
              key={i}
              className={cn(
                "min-h-[1.25em] whitespace-pre-wrap transition-all duration-300",
                i < revealed
                  ? "translate-y-0 opacity-100"
                  : "translate-y-1 opacity-0",
                line.className ?? "text-zinc-400",
              )}
            >
              {line.text}
            </div>
          ))}
          {done ? (
            <span
              aria-hidden
              className="mt-1 inline-block h-4 w-2 translate-y-0.5 animate-pulse bg-accent/80"
            />
          ) : null}
        </div>
      </div>
    </div>
  );
}
