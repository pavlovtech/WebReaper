import { notFound } from "next/navigation";
import { getPage } from "@/lib/pages";
import { MDXContent } from "@/components/mdx-content";

export function LegalPage({ slug }: { slug: string }) {
  const page = getPage(slug);
  if (!page) notFound();

  return (
    <div className="mx-auto max-w-3xl px-4 py-16 sm:px-6 sm:py-20 lg:px-8">
      <h1 className="text-3xl font-semibold tracking-tight sm:text-4xl">
        {page.title}
      </h1>
      {page.updated ? (
        <p className="mt-2 text-sm text-muted-2">
          Last updated{" "}
          {new Date(page.updated).toLocaleDateString("en-US", {
            year: "numeric",
            month: "long",
            day: "numeric",
          })}
        </p>
      ) : null}
      <div className="prose prose-invert mt-8 max-w-none prose-headings:font-semibold prose-headings:tracking-tight prose-a:font-medium prose-a:text-accent prose-a:no-underline hover:prose-a:underline prose-strong:text-foreground">
        <MDXContent code={page.code} />
      </div>
    </div>
  );
}
