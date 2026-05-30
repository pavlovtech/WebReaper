"use client";

import { type FormEvent, useState } from "react";
import { ArrowRight } from "lucide-react";
import { ClimbDemo } from "./climb-demo";
import { TurnstileWidget } from "./turnstile";

// The component streams from the same-origin gate (/api/playground/scrape),
// never the backend directly. Turnstile is shown only when a site key is set;
// without it the gate skips verification (the backend's SSRF guard + shared
// secret are the real protections).
const TURNSTILE_SITE_KEY = process.env.NEXT_PUBLIC_TURNSTILE_SITE_KEY;

export function LiveClimb({ className }: { className?: string }) {
  const [input, setInput] = useState("https://example.com");
  const [submitted, setSubmitted] = useState<string | null>(null);
  // `token` is the fresh widget token (for the next run); `activeToken` is the
  // snapshot the currently-mounted ClimbDemo streams with, so resetting the
  // widget after submit cannot re-fire the in-flight stream.
  const [token, setToken] = useState<string | null>(null);
  const [activeToken, setActiveToken] = useState<string | null>(null);
  // Remount ClimbDemo on each submit so the SSE stream restarts cleanly (and
  // drive a Turnstile reset for a fresh token).
  const [runKey, setRunKey] = useState(0);

  const needsToken = Boolean(TURNSTILE_SITE_KEY);

  const submit = (e: FormEvent) => {
    e.preventDefault();
    const url = input.trim();
    if (!url) return;
    if (needsToken && !token) return; // wait for the challenge to produce a token
    setActiveToken(token);
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
          disabled={needsToken && !token}
          className="inline-flex shrink-0 items-center gap-1.5 rounded-lg bg-accent px-4 py-2.5 text-sm font-medium text-accent-foreground transition hover:bg-accent-strong disabled:cursor-not-allowed disabled:opacity-50"
        >
          Scrape
          <ArrowRight className="h-4 w-4" />
        </button>
      </form>

      {TURNSTILE_SITE_KEY ? (
        <TurnstileWidget
          siteKey={TURNSTILE_SITE_KEY}
          onToken={setToken}
          resetNonce={runKey}
          className="mb-4 flex justify-center"
        />
      ) : null}

      {submitted ? (
        <ClimbDemo key={runKey} liveUrl={submitted} turnstileToken={activeToken ?? undefined} />
      ) : null}
    </div>
  );
}
