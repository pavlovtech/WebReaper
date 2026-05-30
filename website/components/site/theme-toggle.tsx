"use client";

import { useThemeMode } from "flowbite-react";
import { Moon, Sun } from "lucide-react";
import { cn } from "@/lib/utils";

/**
 * Theme toggle with no React state: the correct icon is chosen by CSS from the
 * `.dark` class (set before paint by the inline script in layout.tsx), so there
 * is no hydration mismatch and no flash. flowbite's `toggleMode` flips + persists.
 */
export function ThemeToggle({ className }: { className?: string }) {
  const { toggleMode } = useThemeMode();

  return (
    <button
      type="button"
      aria-label="Toggle color theme"
      onClick={toggleMode}
      className={cn(
        "inline-flex h-9 w-9 items-center justify-center rounded-lg border border-border text-muted transition hover:border-border-strong hover:text-foreground",
        className,
      )}
    >
      <Sun className="hidden h-[18px] w-[18px] dark:block" />
      <Moon className="h-[18px] w-[18px] dark:hidden" />
    </button>
  );
}
