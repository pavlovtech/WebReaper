"use client";

import { useEffect, useRef } from "react";

type TurnstileApi = {
  render: (el: HTMLElement, opts: TurnstileRenderOptions) => string;
  reset: (id?: string) => void;
};

type TurnstileRenderOptions = {
  sitekey: string;
  callback: (token: string) => void;
  "expired-callback"?: () => void;
  "error-callback"?: () => void;
  theme?: "auto" | "light" | "dark";
};

declare global {
  interface Window {
    turnstile?: TurnstileApi;
  }
}

const SCRIPT_SRC =
  "https://challenges.cloudflare.com/turnstile/v0/api.js?render=explicit";

function ensureScript(): Promise<void> {
  if (window.turnstile) return Promise.resolve();
  return new Promise((resolve) => {
    const existing = document.querySelector<HTMLScriptElement>(
      `script[src="${SCRIPT_SRC}"]`,
    );
    if (existing) {
      existing.addEventListener("load", () => resolve(), { once: true });
      if (window.turnstile) resolve();
      return;
    }
    const script = document.createElement("script");
    script.src = SCRIPT_SRC;
    script.async = true;
    script.defer = true;
    script.addEventListener("load", () => resolve(), { once: true });
    document.head.appendChild(script);
  });
}

/**
 * Cloudflare Turnstile widget. Renders once for the configured site key and
 * reports a fresh token via `onToken` (null on expiry/error). Bumping
 * `resetNonce` fetches a new token, since Turnstile tokens are single-use.
 * Rendered only by callers that have a site key, so it is a no-op otherwise.
 */
export function TurnstileWidget({
  siteKey,
  onToken,
  resetNonce,
  className,
}: {
  siteKey: string;
  onToken: (token: string | null) => void;
  resetNonce: number;
  className?: string;
}) {
  const containerRef = useRef<HTMLDivElement>(null);
  const widgetId = useRef<string | null>(null);
  // Keep the latest callback without re-rendering the widget.
  const onTokenRef = useRef(onToken);
  onTokenRef.current = onToken;

  useEffect(() => {
    let cancelled = false;
    void ensureScript().then(() => {
      if (cancelled || widgetId.current || !containerRef.current || !window.turnstile) {
        return;
      }
      widgetId.current = window.turnstile.render(containerRef.current, {
        sitekey: siteKey,
        theme: "dark",
        callback: (token) => onTokenRef.current(token),
        "expired-callback": () => onTokenRef.current(null),
        "error-callback": () => onTokenRef.current(null),
      });
    });
    return () => {
      cancelled = true;
    };
  }, [siteKey]);

  useEffect(() => {
    if (resetNonce > 0 && widgetId.current && window.turnstile) {
      onTokenRef.current(null);
      window.turnstile.reset(widgetId.current);
    }
  }, [resetNonce]);

  return <div ref={containerRef} className={className} />;
}
