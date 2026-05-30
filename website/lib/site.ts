export const siteConfig = {
  name: "WebReaper",
  shortDescription: "AI-native web scraping for .NET",
  description:
    "WebReaper is an AI-native web scraper for .NET. A single ~12 MB binary that turns any site into clean Markdown or structured data, with an LLM layer when you need it. No Docker, no signup, MIT licensed.",
  url: process.env.NEXT_PUBLIC_SITE_URL ?? "https://webreaper.dev",
  ogImage: "/og/default.png",
  version: "10.3.0",
  install: {
    brew: "brew install pavlovtech/webreaper/webreaper",
    curl: "curl -fsSL https://raw.githubusercontent.com/pavlovtech/WebReaper/master/scripts/install.sh | sh",
    nuget: "dotnet add package WebReaper",
  },
  links: {
    github: "https://github.com/pavlovtech/WebReaper",
    nuget: "https://www.nuget.org/packages/WebReaper",
    discussions: "https://github.com/pavlovtech/WebReaper/discussions",
    issues: "https://github.com/pavlovtech/WebReaper/issues",
    license: "https://github.com/pavlovtech/WebReaper/blob/master/LICENSE.txt",
  },
  contactEmail: "business@highcraft.io",
  nav: [
    { title: "Docs", href: "/docs" },
    { title: "Use cases", href: "/use-cases" },
    { title: "Pricing", href: "/pricing" },
    { title: "Blog", href: "/blog" },
  ],
  footerNav: [
    {
      title: "Product",
      links: [
        { title: "Documentation", href: "/docs" },
        { title: "Use cases", href: "/use-cases" },
        { title: "Pricing", href: "/pricing" },
        { title: "Changelog", href: "/blog" },
      ],
    },
    {
      title: "Resources",
      links: [
        { title: "Getting started", href: "/docs/getting-started" },
        { title: "CLI reference", href: "/docs/cli" },
        { title: "AI features", href: "/docs/ai" },
        { title: "Blog", href: "/blog" },
      ],
    },
    {
      title: "Open source",
      links: [
        { title: "GitHub", href: "https://github.com/pavlovtech/WebReaper" },
        { title: "NuGet", href: "https://www.nuget.org/packages/WebReaper" },
        { title: "Discussions", href: "https://github.com/pavlovtech/WebReaper/discussions" },
        { title: "Report an issue", href: "https://github.com/pavlovtech/WebReaper/issues" },
      ],
    },
    {
      title: "Company",
      links: [
        { title: "Privacy", href: "/privacy" },
        { title: "Terms", href: "/terms" },
        { title: "Contact", href: "mailto:business@highcraft.io" },
      ],
    },
  ],
} as const;

export type SiteConfig = typeof siteConfig;
