"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { Search, X } from "lucide-react";
import type { SearchItem } from "@/lib/search";
import { cn } from "@/lib/utils";

export function SearchCommand({ index }: { index: SearchItem[] }) {
  const router = useRouter();
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [active, setActive] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);

  const results = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return index.slice(0, 8);
    return index
      .map((it) => {
        const t = it.title.toLowerCase();
        const d = it.description.toLowerCase();
        let s = 0;
        if (t.includes(q)) s += 5;
        if (t.startsWith(q)) s += 3;
        if (d.includes(q)) s += 1;
        if (it.group.toLowerCase().includes(q)) s += 1;
        return { it, s };
      })
      .filter((x) => x.s > 0)
      .sort((a, b) => b.s - a.s)
      .slice(0, 10)
      .map((x) => x.it);
  }, [query, index]);

  useEffect(() => setActive(0), [query]);

  const close = useCallback(() => {
    setOpen(false);
    setQuery("");
  }, []);

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setOpen((v) => !v);
      } else if (e.key === "Escape") {
        close();
      }
    }
    function onOpen() {
      setOpen(true);
    }
    window.addEventListener("keydown", onKey);
    window.addEventListener("webreaper:search", onOpen as EventListener);
    return () => {
      window.removeEventListener("keydown", onKey);
      window.removeEventListener("webreaper:search", onOpen as EventListener);
    };
  }, [close]);

  useEffect(() => {
    if (open) {
      const t = setTimeout(() => inputRef.current?.focus(), 10);
      return () => clearTimeout(t);
    }
  }, [open]);

  function go(url: string) {
    close();
    router.push(url);
  }

  function onInputKey(e: React.KeyboardEvent) {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setActive((a) => Math.min(a + 1, results.length - 1));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setActive((a) => Math.max(a - 1, 0));
    } else if (e.key === "Enter") {
      const r = results[active];
      if (r) go(r.url);
    }
  }

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-[60] flex items-start justify-center p-4 pt-[12vh]">
      <div
        className="absolute inset-0 bg-black/70 backdrop-blur-sm"
        onClick={close}
      />
      <div className="relative w-full max-w-xl overflow-hidden rounded-2xl border border-border bg-surface shadow-2xl">
        <div className="flex items-center gap-3 border-b border-border px-4">
          <Search className="h-4 w-4 shrink-0 text-muted-2" />
          <input
            ref={inputRef}
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={onInputKey}
            placeholder="Search docs, use cases, blog…"
            className="h-12 w-full bg-transparent text-sm outline-none placeholder:text-muted-2"
          />
          <button
            onClick={close}
            aria-label="Close search"
            className="shrink-0 text-muted-2 hover:text-foreground"
          >
            <X className="h-4 w-4" />
          </button>
        </div>
        <div className="max-h-80 overflow-y-auto p-2">
          {results.length === 0 ? (
            <p className="px-3 py-6 text-center text-sm text-muted">
              No results for &ldquo;{query}&rdquo;.
            </p>
          ) : (
            results.map((r, i) => (
              <button
                key={r.url}
                onMouseEnter={() => setActive(i)}
                onClick={() => go(r.url)}
                className={cn(
                  "flex w-full flex-col items-start gap-0.5 rounded-lg px-3 py-2.5 text-left transition",
                  i === active ? "bg-accent/10" : "hover:bg-surface-2",
                )}
              >
                <span className="flex items-center gap-2 text-sm font-medium text-foreground">
                  {r.title}
                  <span className="rounded bg-surface-2 px-1.5 py-0.5 text-[10px] uppercase tracking-wide text-muted-2">
                    {r.group}
                  </span>
                </span>
                {r.description ? (
                  <span className="line-clamp-1 text-xs text-muted">
                    {r.description}
                  </span>
                ) : null}
              </button>
            ))
          )}
        </div>
      </div>
    </div>
  );
}
