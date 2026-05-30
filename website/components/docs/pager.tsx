import Link from "next/link";
import { ArrowLeft, ArrowRight } from "lucide-react";

type PagerLink = { title: string; url: string } | null;

export function DocsPager({ prev, next }: { prev: PagerLink; next: PagerLink }) {
  return (
    <div className="mt-14 grid gap-4 border-t border-border pt-8 sm:grid-cols-2">
      {prev ? (
        <Link
          href={prev.url}
          className="group surface-card flex flex-col p-4 transition hover:border-accent/40"
        >
          <span className="flex items-center gap-1.5 text-xs text-muted-2">
            <ArrowLeft className="h-3.5 w-3.5" /> Previous
          </span>
          <span className="mt-1 font-medium text-foreground group-hover:text-accent">
            {prev.title}
          </span>
        </Link>
      ) : (
        <span />
      )}
      {next ? (
        <Link
          href={next.url}
          className="group surface-card flex flex-col items-end p-4 text-right transition hover:border-accent/40 sm:col-start-2"
        >
          <span className="flex items-center gap-1.5 text-xs text-muted-2">
            Next <ArrowRight className="h-3.5 w-3.5" />
          </span>
          <span className="mt-1 font-medium text-foreground group-hover:text-accent">
            {next.title}
          </span>
        </Link>
      ) : null}
    </div>
  );
}
