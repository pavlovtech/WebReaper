import Link from "next/link";
import { notFound } from "next/navigation";
import type { Metadata } from "next";
import { ChevronRight } from "lucide-react";
import { getDocBySlug, getFlatDocs, getPager } from "@/lib/docs";
import { MDXContent } from "@/components/mdx-content";
import { DocsToc } from "@/components/docs/toc";
import { DocsPager } from "@/components/docs/pager";
import { siteConfig } from "@/lib/site";

type Params = Promise<{ slug: string[] }>;

export function generateStaticParams() {
  return getFlatDocs().map((d) => ({ slug: d.slug.split("/") }));
}

export async function generateMetadata({
  params,
}: {
  params: Params;
}): Promise<Metadata> {
  const { slug } = await params;
  const doc = getDocBySlug(slug.join("/"));
  if (!doc) return {};
  const url = `${siteConfig.url}${doc.url}`;
  return {
    title: doc.title,
    description: doc.description,
    alternates: { canonical: url },
    openGraph: {
      title: doc.title,
      description: doc.description,
      url,
      type: "article",
    },
  };
}

export default async function DocPage({ params }: { params: Params }) {
  const { slug } = await params;
  const doc = getDocBySlug(slug.join("/"));
  if (!doc) notFound();

  const pager = getPager(doc.slug);

  return (
    <div className="xl:grid xl:grid-cols-[minmax(0,1fr)_220px] xl:gap-10">
      <article className="min-w-0 py-10 lg:py-12">
        <nav className="flex items-center gap-1.5 text-xs text-muted-2">
          <Link href="/docs" className="hover:text-foreground">
            Docs
          </Link>
          <ChevronRight className="h-3 w-3" />
          <span className="text-muted">{doc.category}</span>
        </nav>

        <header className="mt-4 border-b border-border pb-8">
          <h1 className="text-3xl font-semibold tracking-tight sm:text-4xl">
            {doc.title}
          </h1>
          {doc.description ? (
            <p className="mt-3 text-lg text-muted">{doc.description}</p>
          ) : null}
        </header>

        <div className="prose dark:prose-invert mt-8 max-w-none prose-headings:scroll-mt-24 prose-headings:font-semibold prose-headings:tracking-tight prose-h2:mt-10 prose-h2:border-b prose-h2:border-border prose-h2:pb-2 prose-a:font-medium prose-a:text-accent prose-a:no-underline hover:prose-a:underline prose-strong:text-foreground prose-li:marker:text-muted-2">
          <MDXContent code={doc.code} />
        </div>

        <DocsPager prev={pager.prev} next={pager.next} />
      </article>

      <DocsToc toc={doc.toc as never} />
    </div>
  );
}
