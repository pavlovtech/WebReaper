"use client";

import { useEffect, useState } from "react";
import { useThemeMode } from "flowbite-react";
import { Moon, Sun } from "lucide-react";
import { cn } from "@/lib/utils";

export function ThemeToggle({ className }: { className?: string }) {
  const { computedMode, setMode } = useThemeMode();
  const [mounted, setMounted] = useState(false);

  useEffect(() => setMounted(true), []);

  const isDark = computedMode === "dark";

  return (
    <button
      type="button"
      aria-label="Toggle color theme"
      onClick={() => setMode(isDark ? "light" : "dark")}
      className={cn(
        "inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border text-muted transition hover:border-border-strong hover:text-foreground",
        className,
      )}
    >
      {mounted && isDark ? (
        <Sun className="h-[18px] w-[18px]" />
      ) : (
        <Moon className="h-[18px] w-[18px]" />
      )}
    </button>
  );
}
