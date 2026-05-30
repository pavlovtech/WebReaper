export type PlanAction = "install" | "subscribe" | "contact";

export type Plan = {
  id: string;
  name: string;
  badge?: string;
  priceMonthly: number | null; // null = custom / contact
  priceAnnual: number | null; // per-month price when billed annually
  customPrice?: string;
  tagline: string;
  features: string[];
  cta: string;
  action: PlanAction;
  href?: string;
  featured?: boolean;
};

export const plans: Plan[] = [
  {
    id: "oss",
    name: "Open Source",
    priceMonthly: 0,
    priceAnnual: 0,
    tagline: "The full library, CLI, and Claude Code skill. Yours forever.",
    features: [
      "Complete .NET library + AOT CLI",
      "Markdown, schema, and AI extraction",
      "Bring your own LLM key",
      "All open-source satellites (Redis, Mongo, Playwright, CDP…)",
      "Self-hosted, runs anywhere",
      "MIT licensed",
      "Community support",
    ],
    cta: "Install now",
    action: "install",
    href: "/docs/getting-started",
  },
  {
    id: "cloud",
    name: "Cloud",
    badge: "Early access",
    priceMonthly: 29,
    priceAnnual: 24,
    tagline: "Hosted crawls and managed infrastructure, without the ops.",
    features: [
      "Everything in Open Source",
      "Hosted, scheduled crawls",
      "Managed proxy pool",
      "Hosted stealth browsers",
      "Web dashboard + run history",
      "Team workspace",
      "Storage and exports",
      "Email support",
    ],
    cta: "Join the waitlist",
    action: "subscribe",
    featured: true,
  },
  {
    id: "enterprise",
    name: "Enterprise",
    priceMonthly: null,
    priceAnnual: null,
    customPrice: "Custom",
    tagline: "For teams with scale, security, and compliance needs.",
    features: [
      "Everything in Cloud",
      "SSO / SAML",
      "SLA + priority support",
      "On-prem / air-gapped deploys",
      "Private stealth satellites",
      "Security review + DPA",
      "Dedicated engineer",
    ],
    cta: "Contact sales",
    action: "contact",
  },
];

export const pricingFaqs = [
  {
    q: "Is the open-source version really free?",
    a: "Yes. The library, CLI, and Claude Code skill are MIT licensed and free forever, with every extraction and AI feature included. You bring your own LLM key.",
  },
  {
    q: "What is WebReaper Cloud?",
    a: "An optional hosted layer for teams who would rather not run infrastructure: scheduled crawls, managed proxies, hosted stealth browsers, a dashboard, and team features. The open-source core stays free.",
  },
  {
    q: "When does Cloud launch?",
    a: "Cloud is in early access. The prices shown are indicative; join the waitlist and we will email you before billing starts. No credit card is needed to join.",
  },
  {
    q: "Can I self-host the paid features?",
    a: "Most building blocks (distributed backends, stealth, scheduling via cron) are already in the open-source project. Cloud bundles and manages them for you.",
  },
  {
    q: "How is Enterprise different?",
    a: "Enterprise adds SSO, SLAs, on-prem and air-gapped deployment, private satellites, a security review, and a dedicated engineer. Reach out and we will scope it with you.",
  },
];
