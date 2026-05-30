"use client";

import { useEffect, useState } from "react";
import { cn } from "@/lib/utils";

type TocItem = { title: string; url: string; items?: TocItem[] };

function flatten(items: TocItem[]): TocItem[] {
  return items.flatMap((i) => [i, ...(i.items ? flatten(i.items) : [])]);
}

export function DocsToc({ toc }: { toc: TocItem[] }) {
  const items = flatten(toc ?? []);
  const [active, setActive] = useState<string>("");

  useEffect(() => {
    if (items.length === 0) return;
    const ids = items
      .map((i) => i.url.replace(/^#/, ""))
      .filter(Boolean);

    const observer = new IntersectionObserver(
      (entries) => {
        for (const entry of entries) {
          if (entry.isIntersecting) {
            setActive(entry.target.id);
            break;
          }
        }
      },
      { rootMargin: "0% 0% -80% 0%", threshold: 1 },
    );

    for (const id of ids) {
      const el = document.getElementById(id);
      if (el) observer.observe(el);
    }
    return () => observer.disconnect();
  }, [items]);

  if (items.length === 0) return null;

  return (
    <div className="sticky top-24 hidden max-h-[calc(100dvh-7rem)] overflow-y-auto py-12 xl:block">
      <p className="mb-3 text-xs font-semibold uppercase tracking-wider text-muted-2">
        On this page
      </p>
      <ul className="space-y-2 border-l border-border text-sm">
        {items.map((item) => {
          const id = item.url.replace(/^#/, "");
          const isActive = active === id;
          return (
            <li key={item.url}>
              <a
                href={item.url}
                className={cn(
                  "-ml-px block border-l py-0.5 pl-4 transition-colors",
                  isActive
                    ? "border-accent text-accent"
                    : "border-transparent text-muted hover:text-foreground",
                )}
              >
                {item.title}
              </a>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
