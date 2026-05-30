"use client";

import { useState } from "react";
import { CLIMB_SCRIPTS } from "@/lib/playground/climb-events";
import { ClimbDemo } from "./climb-demo";
import { cn } from "@/lib/utils";

export function PlaygroundShowcase({ className }: { className?: string }) {
  const [activeId, setActiveId] = useState(CLIMB_SCRIPTS[0].id);
  const script = CLIMB_SCRIPTS.find((s) => s.id === activeId) ?? CLIMB_SCRIPTS[0];

  return (
    <div className={className}>
      <div
        className="mx-auto mb-3 flex w-fit items-center gap-1 rounded-lg border border-border bg-surface/60 p-1 backdrop-blur"
        role="tablist"
        aria-label="Choose a scenario"
      >
        {CLIMB_SCRIPTS.map((s) => (
          <button
            key={s.id}
            type="button"
            role="tab"
            aria-selected={activeId === s.id}
            onClick={() => setActiveId(s.id)}
            className={cn(
              "rounded-md px-3.5 py-1.5 text-sm font-medium transition",
              activeId === s.id
                ? "bg-accent/15 text-accent"
                : "text-muted hover:text-foreground",
            )}
          >
            {s.label}
          </button>
        ))}
      </div>

      {/* Switching tabs swaps the script prop, which re-fires the playback. */}
      <ClimbDemo script={script} />

      <p className="mx-auto mt-3 max-w-md text-center text-sm text-muted">
        {script.blurb}
      </p>
    </div>
  );
}
