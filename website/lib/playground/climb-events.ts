/**
 * The escalation-climb event model for the playground viz.
 *
 * This is the single source-agnostic contract: the canned landing-hero demo
 * (this file's scripts) and the future live Tier B stream both produce the same
 * `ClimbEvent`s. The live path will forward the per-tier events the core
 * `EscalatingPageLoader` already logs ("Loading … at tier {Tier}", "Page …
 * still blocked at the top tier: {Reason}") over SSE; the UI never knows or
 * cares whether the source is a recording or a socket. See
 * docs/CLOUD-SCRAPE-PLAYGROUND-PLAN.md (Phase 0).
 */

export type TierName = "http" | "browser" | "stealth";

/** Render order, lowest rung to highest, drives the ladder layout. */
export const TIER_ORDER: readonly TierName[] = ["http", "browser", "stealth"];

export const TIER_LABEL: Record<TierName, string> = {
  http: "HTTP",
  browser: "Headless browser",
  stealth: "Stealth browser",
};

export type ClimbEvent =
  | { kind: "request"; url: string }
  | { kind: "attempt"; tier: TierName }
  | { kind: "blocked"; tier: TierName; status?: number; reason: string }
  | { kind: "escalate"; from: TierName; to: TierName }
  | { kind: "success"; tier: TierName; status: number }
  | { kind: "result"; title: string; markdown: string }
  | { kind: "exhausted"; tier: TierName; reason: string };

/** A `ClimbEvent` plus its millisecond offset from the start of playback. */
export type TimedEvent = { at: number; event: ClimbEvent };

export type ClimbScript = {
  id: string;
  /** Tab label on the demo page. */
  label: string;
  /** One-line description of the outcome. */
  blurb: string;
  events: TimedEvent[];
};

const CRACKED_MARKDOWN = `# Acme Pricing

Simple, usage-based pricing.

- **Hobby**: $0/mo, 500 pages
- **Pro**: $49/mo, 100k pages
- **Scale**: custom`;

/** The hero: a real-shaped climb that beats Cloudflare at the stealth rung. */
const cloudflareCracked: ClimbScript = {
  id: "cloudflare",
  label: "Cloudflare (cracked)",
  blurb: "HTTP and a vanilla browser are challenged; the stealth rung gets through.",
  events: [
    { at: 200, event: { kind: "request", url: "https://acme.example/pricing" } },
    { at: 700, event: { kind: "attempt", tier: "http" } },
    { at: 1500, event: { kind: "blocked", tier: "http", status: 403, reason: "Cloudflare challenge (cf-mitigated)" } },
    { at: 1900, event: { kind: "escalate", from: "http", to: "browser" } },
    { at: 2400, event: { kind: "attempt", tier: "browser" } },
    { at: 3500, event: { kind: "blocked", tier: "browser", reason: "Challenge still presented (cf-chl-bypass)" } },
    { at: 3900, event: { kind: "escalate", from: "browser", to: "stealth" } },
    { at: 4400, event: { kind: "attempt", tier: "stealth" } },
    { at: 5600, event: { kind: "success", tier: "stealth", status: 200 } },
    { at: 6100, event: { kind: "result", title: "Acme Pricing", markdown: CRACKED_MARKDOWN } },
  ],
};

/** The honest-loss case (grill decision #4): still blocked at the top rung. */
const dataDomeBlocked: ClimbScript = {
  id: "datadome",
  label: "DataDome (still blocked)",
  blurb: "Every rung is challenged. WebReaper reports the block instead of returning garbage.",
  events: [
    { at: 200, event: { kind: "request", url: "https://shop.example/products" } },
    { at: 700, event: { kind: "attempt", tier: "http" } },
    { at: 1500, event: { kind: "blocked", tier: "http", status: 403, reason: "DataDome challenge" } },
    { at: 1900, event: { kind: "escalate", from: "http", to: "browser" } },
    { at: 2400, event: { kind: "attempt", tier: "browser" } },
    { at: 3500, event: { kind: "blocked", tier: "browser", reason: "Device-check interstitial" } },
    { at: 3900, event: { kind: "escalate", from: "browser", to: "stealth" } },
    { at: 4400, event: { kind: "attempt", tier: "stealth" } },
    { at: 5800, event: { kind: "exhausted", tier: "stealth", reason: "Still challenged at the top tier" } },
  ],
};

export const CLIMB_SCRIPTS: readonly ClimbScript[] = [cloudflareCracked, dataDomeBlocked];

/**
 * Schedule a timed script onto `onEvent`. Returns a cleanup that cancels any
 * pending events. The live SSE source will call the same `onEvent` directly, so
 * the consumer (the reducer in climb-demo) is identical for both paths.
 */
export function playScript(
  events: TimedEvent[],
  onEvent: (event: ClimbEvent) => void,
): () => void {
  const timers = events.map(({ at, event }) =>
    setTimeout(() => onEvent(event), at),
  );
  return () => timers.forEach(clearTimeout);
}
