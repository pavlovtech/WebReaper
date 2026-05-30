"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { Menu, Search, X } from "lucide-react";
import { siteConfig } from "@/lib/site";
import { cn } from "@/lib/utils";
import { GitHubIcon } from "@/components/icons";
import { Logo } from "./logo";
import { ThemeToggle } from "./theme-toggle";
import { Button } from "@/components/ui/button";

export function Navbar() {
  const pathname = usePathname();
  const [scrolled, setScrolled] = useState(false);
  const [open, setOpen] = useState(false);

  useEffect(() => {
    const onScroll = () => setScrolled(window.scrollY > 8);
    onScroll();
    window.addEventListener("scroll", onScroll, { passive: true });
    return () => window.removeEventListener("scroll", onScroll);
  }, []);

  return (
    <header
      className={cn(
        "sticky top-0 z-50 w-full border-b transition-colors duration-300",
        scrolled
          ? "border-border bg-background/80 backdrop-blur-xl"
          : "border-transparent bg-transparent",
      )}
    >
      <nav className="mx-auto flex h-16 max-w-7xl items-center justify-between gap-4 px-4 sm:px-6 lg:px-8">
        <div className="flex items-center gap-8">
          <Link href="/" aria-label="WebReaper home">
            <Logo />
          </Link>
          <ul className="hidden items-center gap-1 md:flex">
            {siteConfig.nav.map((item) => {
              const active = pathname.startsWith(item.href);
              return (
                <li key={item.href}>
                  <Link
                    href={item.href}
                    className={cn(
                      "rounded-lg px-3 py-2 text-sm transition-colors",
                      active
                        ? "text-foreground"
                        : "text-muted hover:text-foreground",
                    )}
                  >
                    {item.title}
                  </Link>
                </li>
              );
            })}
          </ul>
        </div>

        <div className="hidden items-center gap-2 md:flex">
          <button
            type="button"
            aria-label="Search"
            onClick={() => window.dispatchEvent(new Event("webreaper:search"))}
            className="inline-flex h-9 items-center gap-2 rounded-lg border border-border px-3 text-sm text-muted-2 transition hover:border-border-strong hover:text-foreground"
          >
            <Search className="h-4 w-4" />
            <span className="hidden lg:inline">Search</span>
            <kbd className="hidden rounded border border-border bg-surface-2 px-1.5 font-mono text-[10px] text-muted lg:inline">
              ⌘K
            </kbd>
          </button>
          <ThemeToggle />
          <a
            href={siteConfig.links.github}
            target="_blank"
            rel="noreferrer noopener"
            className="inline-flex h-9 items-center gap-2 rounded-lg border border-border px-3 text-sm text-muted transition hover:border-border-strong hover:text-foreground"
          >
            <GitHubIcon className="h-4 w-4" />
            <span className="hidden lg:inline">Star</span>
          </a>
          <Button href="/docs/getting-started" size="sm">
            Get started
          </Button>
        </div>

        <button
          type="button"
          aria-label="Toggle menu"
          aria-expanded={open}
          aria-controls="mobile-menu"
          onClick={() => setOpen((v) => !v)}
          className="inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border text-foreground md:hidden"
        >
          {open ? <X className="h-5 w-5" /> : <Menu className="h-5 w-5" />}
        </button>
      </nav>

      {open ? (
        <div id="mobile-menu" className="border-t border-border bg-background/95 backdrop-blur-xl md:hidden">
          <ul className="space-y-1 px-4 py-4">
            {siteConfig.nav.map((item) => (
              <li key={item.href}>
                <Link
                  href={item.href}
                  onClick={() => setOpen(false)}
                  className="block rounded-lg px-3 py-2.5 text-sm text-muted hover:bg-surface-2 hover:text-foreground"
                >
                  {item.title}
                </Link>
              </li>
            ))}
            <li className="flex items-center gap-2 px-1 pt-3">
              <ThemeToggle />
              <Button href="/docs/getting-started" size="sm" className="flex-1">
                Get started
              </Button>
            </li>
          </ul>
        </div>
      ) : null}
    </header>
  );
}
