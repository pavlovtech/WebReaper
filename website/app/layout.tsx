import type { Metadata } from "next";
import { Geist, Geist_Mono } from "next/font/google";
import "./globals.css";
import { siteConfig } from "@/lib/site";
import { Navbar } from "@/components/site/navbar";
import { Footer } from "@/components/site/footer";
import { SearchCommand } from "@/components/search/search-command";
import { JsonLd } from "@/components/json-ld";
import { getSearchIndex } from "@/lib/search";

const geistSans = Geist({ variable: "--font-geist-sans", subsets: ["latin"] });
const geistMono = Geist_Mono({
  variable: "--font-geist-mono",
  subsets: ["latin"],
});

// Dark-first: default to dark unless the visitor explicitly chose light.
// Uses flowbite's storage key so useThemeMode stays in sync (no FOUC).
const themeScript = `try{var m=localStorage.getItem('flowbite-theme-mode');if(!m){m='dark';localStorage.setItem('flowbite-theme-mode','dark');}document.documentElement.classList.toggle('dark',m!=='light');}catch(e){document.documentElement.classList.add('dark');}`;

export const metadata: Metadata = {
  metadataBase: new URL(siteConfig.url),
  title: {
    default: `${siteConfig.name}: ${siteConfig.shortDescription}`,
    template: `%s | ${siteConfig.name}`,
  },
  description: siteConfig.description,
  keywords: [
    "web scraping",
    ".NET scraper",
    "C# web crawler",
    "AI web scraping",
    "LLM data extraction",
    "Markdown scraper",
    "Firecrawl alternative",
    "WebReaper",
  ],
  creator: "WebReaper",
  openGraph: {
    type: "website",
    locale: "en_US",
    url: siteConfig.url,
    siteName: siteConfig.name,
    title: `${siteConfig.name}: ${siteConfig.shortDescription}`,
    description: siteConfig.description,
  },
  twitter: {
    card: "summary_large_image",
    title: `${siteConfig.name}: ${siteConfig.shortDescription}`,
    description: siteConfig.description,
  },
  robots: { index: true, follow: true },
};

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html
      lang="en"
      suppressHydrationWarning
      className={`${geistSans.variable} ${geistMono.variable}`}
    >
      <head>
        <script dangerouslySetInnerHTML={{ __html: themeScript }} />
      </head>
      <body className="flex min-h-dvh flex-col bg-background font-sans text-foreground antialiased">
        <a
          href="#main"
          className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-[100] focus:rounded-lg focus:bg-accent focus:px-4 focus:py-2 focus:text-sm focus:font-medium focus:text-accent-foreground"
        >
          Skip to content
        </a>
        <JsonLd />
        <Navbar />
        <main id="main" className="flex-1">
          {children}
        </main>
        <Footer />
        <SearchCommand index={getSearchIndex()} />
      </body>
    </html>
  );
}
