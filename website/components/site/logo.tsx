import { cn } from "@/lib/utils";

/** WebReaper wordmark: a small crawl-graph mark (nodes + links) on an accent tile. */
export function Logo({
  className,
  showWordmark = true,
}: {
  className?: string;
  showWordmark?: boolean;
}) {
  return (
    <span className={cn("inline-flex items-center gap-2.5", className)}>
      <span className="relative grid h-8 w-8 place-items-center rounded-lg bg-gradient-to-br from-accent to-emerald-600 shadow-[0_0_24px_-6px] shadow-accent/60">
        <svg
          viewBox="0 0 24 24"
          className="h-5 w-5 text-white"
          fill="none"
          stroke="currentColor"
          strokeWidth="1.9"
          strokeLinecap="round"
          strokeLinejoin="round"
          aria-hidden="true"
        >
          <circle cx="5" cy="6" r="1.7" />
          <circle cx="19" cy="6" r="1.7" />
          <circle cx="12" cy="18" r="1.7" />
          <path d="M6.5 6.9 10.9 16.4M17.5 6.9 13.1 16.4M6.7 6h10.6" />
        </svg>
      </span>
      {showWordmark ? (
        <span className="text-[15px] font-semibold tracking-tight text-foreground">
          WebReaper
        </span>
      ) : null}
    </span>
  );
}
