import type { Metadata } from "next";
import { PricingTable } from "@/components/pricing/pricing-table";
import { pricingFaqs } from "@/lib/pricing";
import { siteConfig } from "@/lib/site";

export const metadata: Metadata = {
  title: "Pricing",
  description:
    "WebReaper is free and open source. Optional Cloud and Enterprise tiers add hosted crawls, managed infrastructure, and team features.",
};

const container = "mx-auto max-w-6xl px-4 sm:px-6 lg:px-8";

const jsonLd = {
  "@context": "https://schema.org",
  "@type": "Product",
  name: "WebReaper",
  description: siteConfig.description,
  brand: { "@type": "Brand", name: "WebReaper" },
  offers: [
    {
      "@type": "Offer",
      name: "Open Source",
      price: "0",
      priceCurrency: "USD",
      description: "The full library, CLI, and Claude Code skill. MIT licensed.",
    },
    {
      "@type": "Offer",
      name: "Cloud",
      price: "29",
      priceCurrency: "USD",
      description: "Hosted crawls and managed infrastructure (early access).",
    },
  ],
};

export default function PricingPage() {
  return (
    <div className="relative">
      <div className="pointer-events-none absolute inset-x-0 top-0 -z-10 h-96 glow-accent" />
      <section className={`${container} py-16 sm:py-24`}>
        <div className="mx-auto max-w-2xl text-center">
          <h1 className="text-4xl font-semibold tracking-tight sm:text-5xl">
            Free to run. Pay only to scale.
          </h1>
          <p className="mt-4 text-lg text-muted">
            The open-source core does everything locally, forever. Hosted tiers
            add scheduling, managed infrastructure, and team features when you
            need them.
          </p>
        </div>

        <div className="mt-14">
          <PricingTable />
        </div>

        <div className="mx-auto mt-24 max-w-3xl">
          <h2 className="text-center text-2xl font-semibold tracking-tight">
            Pricing questions
          </h2>
          <div className="mt-8 divide-y divide-border border-y border-border">
            {pricingFaqs.map((faq) => (
              <details key={faq.q} className="group py-5">
                <summary className="flex cursor-pointer list-none items-center justify-between gap-4 font-medium text-foreground">
                  {faq.q}
                  <span className="text-muted transition group-open:rotate-45">
                    <svg
                      width="18"
                      height="18"
                      viewBox="0 0 24 24"
                      fill="none"
                      stroke="currentColor"
                      strokeWidth="2"
                      strokeLinecap="round"
                    >
                      <path d="M12 5v14M5 12h14" />
                    </svg>
                  </span>
                </summary>
                <p className="mt-3 leading-relaxed text-muted">{faq.a}</p>
              </details>
            ))}
          </div>
        </div>
      </section>

      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: JSON.stringify(jsonLd) }}
      />
    </div>
  );
}
