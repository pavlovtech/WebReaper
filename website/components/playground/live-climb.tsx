"use client";

import { type FormEvent, useState } from "react";
import { ArrowRight } from "lucide-react";
import { ClimbDemo } from "./climb-demo";

// The Tier A backend (cloud/WebReaper.PlaygroundApi). Defaults to the local
// dev port; set NEXT_PUBLIC_PLAYGROUND_API to the deployed origin later.
const API_BASE = process.env.NEXT_PUBLIC_PLAYGROUND_API ?? "http://localhost:5179";

export function LiveClimb({ className }: { className?: string }) {
  const [input, setInput] = useState("https://example.com");
  const [submitted, setSubmitted] = useState<string | null>(null);
  // Remount ClimbDemo on each submit so the SSE stream restarts cleanly,
  // even when the URL is unchanged.
  const [runKey, setRunKey] = useState(0);

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const url = input.trim();
    if (!url) return;
    setSubmitted(url);
    setRunKey((n) => n + 1);
  };

  return (
    <div className={className}>
      <form onSubmit={submit} className="mx-auto mb-4 flex max-w-xl items-center gap-2">
        <input
          type="url"
          value={input}
          onChange={(e) => setInput(e.target.value)}
          placeholder="https://example.com"
          aria-label="URL to scrape"
          className="min-w-0 flex-1 rounded-lg border border-border bg-surface/60 px-4 py-2.5 font-mono text-sm text-foreground outline-none transition placeholder:text-muted-2 focus:border-accent/50"
        />
        <button
          type="submit"
          className="inline-flex shrink-0 items-center gap-1.5 rounded-lg bg-accent px-4 py-2.5 text-sm font-medium text-accent-foreground transition hover:bg-accent-strong"
        >
          Scrape
          <ArrowRight className="h-4 w-4" />
        </button>
      </form>

      {submitted ? <ClimbDemo key={runKey} liveUrl={submitted} apiBase={API_BASE} /> : null}
    </div>
  );
}
