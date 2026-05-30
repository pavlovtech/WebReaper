"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { Menu, X } from "lucide-react";
import type { DocsNavGroup } from "@/lib/docs";
import { cn } from "@/lib/utils";

function NavList({
  nav,
  pathname,
  onNavigate,
}: {
  nav: DocsNavGroup[];
  pathname: string;
  onNavigate?: () => void;
}) {
  return (
    <nav className="space-y-7">
      {nav.map((group) => (
        <div key={group.category}>
          <p className="mb-2 text-xs font-semibold uppercase tracking-wider text-muted-2">
            {group.category}
          </p>
          <ul className="space-y-0.5 border-l border-border">
            {group.items.map((item) => {
              const active = pathname === item.url;
              return (
                <li key={item.url}>
                  <Link
                    href={item.url}
                    onClick={onNavigate}
                    className={cn(
                      "-ml-px block border-l py-1.5 pl-4 text-sm transition-colors",
                      active
                        ? "border-accent font-medium text-accent"
                        : "border-transparent text-muted hover:border-border-strong hover:text-foreground",
                    )}
                  >
                    {item.title}
                  </Link>
                </li>
              );
            })}
          </ul>
        </div>
      ))}
    </nav>
  );
}

export function DocsSidebar({ nav }: { nav: DocsNavGroup[] }) {
  const pathname = usePathname();
  const [open, setOpen] = useState(false);
  const closeRef = useRef<HTMLButtonElement>(null);

  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") setOpen(false);
    };
    window.addEventListener("keydown", onKey);
    const t = setTimeout(() => closeRef.current?.focus(), 10);
    const prevOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      window.removeEventListener("keydown", onKey);
      clearTimeout(t);
      document.body.style.overflow = prevOverflow;
    };
  }, [open]);

  return (
    <>
      {/* Mobile trigger */}
      <div className="flex items-center justify-between py-4 lg:hidden">
        <button
          type="button"
          onClick={() => setOpen(true)}
          className="inline-flex items-center gap-2 rounded-lg border border-border px-3 py-2 text-sm text-muted"
        >
          <Menu className="h-4 w-4" /> Menu
        </button>
      </div>

      {/* Mobile drawer */}
      {open ? (
        <div className="fixed inset-0 z-50 lg:hidden">
          <div
            className="absolute inset-0 bg-black/60 backdrop-blur-sm"
            onClick={() => setOpen(false)}
          />
          <div
            role="dialog"
            aria-modal="true"
            aria-label="Documentation navigation"
            className="absolute left-0 top-0 h-full w-80 max-w-[85%] overflow-y-auto border-r border-border bg-background p-6"
          >
            <div className="mb-6 flex items-center justify-between">
              <span className="text-sm font-semibold">Documentation</span>
              <button
                ref={closeRef}
                type="button"
                onClick={() => setOpen(false)}
                aria-label="Close menu"
                className="rounded-lg border border-border p-1.5 text-muted"
              >
                <X className="h-4 w-4" />
              </button>
            </div>
            <NavList
              nav={nav}
              pathname={pathname}
              onNavigate={() => setOpen(false)}
            />
          </div>
        </div>
      ) : null}

      {/* Desktop sidebar */}
      <aside className="hidden lg:block">
        <div className="sticky top-24 max-h-[calc(100dvh-7rem)] overflow-y-auto py-12 pr-4">
          <NavList nav={nav} pathname={pathname} />
        </div>
      </aside>
    </>
  );
}
