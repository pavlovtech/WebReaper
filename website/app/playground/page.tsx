import type { Metadata } from "next";
import Link from "next/link";
import { Shield } from "lucide-react";
import { PlaygroundShowcase } from "@/components/playground/playground-showcase";

export const metadata: Metadata = {
  title: "Playground",
  description:
    "Watch WebReaper climb from HTTP to a headless browser to a stealth browser to get past bot protection, live.",
  // Phase 0 preview surface: not linked in nav and not ready to index yet.
  robots: { index: false, follow: false },
};

export default function PlaygroundPage() {
  return (
    <div className="relative">
      <div className="glow-accent pointer-events-none absolute inset-x-0 top-0 h-72" aria-hidden />
      <section className="relative mx-auto max-w-3xl px-4 py-16 sm:py-24">
        <div className="text-center">
          <span className="inline-flex items-center gap-1.5 rounded-full border border-border bg-surface/60 px-3 py-1 text-xs font-medium text-muted backdrop-blur">
            <Shield className="h-3.5 w-3.5 text-accent" />
            Playground · preview
          </span>
          <h1 className="text-gradient mt-5 text-4xl font-semibold tracking-tight sm:text-5xl">
            Watch the climb
          </h1>
          <p className="mx-auto mt-4 max-w-xl text-pretty text-muted">
            Bot protection blocks a plain HTTP fetch. WebReaper escalates per page,
            HTTP to a headless browser to a stealth browser, and stops the moment one
            rung gets through. Here is what that looks like.
          </p>
        </div>

        <PlaygroundShowcase className="mt-10" />

        <p className="mx-auto mt-8 max-w-xl text-center text-sm text-muted-2">
          This is a recorded run for now (Phase 0). The live version streams the same
          events from a real scrape of a URL you paste.{" "}
          <Link
            href="https://github.com/pavlovtech/WebReaper/blob/master/docs/CLOUD-SCRAPE-PLAYGROUND-PLAN.md"
            className="text-accent underline-offset-4 hover:underline"
          >
            See the design
          </Link>
          .
        </p>
      </section>
    </div>
  );
}
