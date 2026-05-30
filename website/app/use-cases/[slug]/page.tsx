import Link from "next/link";
import { notFound } from "next/navigation";
import type { Metadata } from "next";
import { ArrowLeft, ArrowRight } from "lucide-react";
import { getUseCaseBySlug, getUseCases } from "@/lib/use-cases";
import { MDXContent } from "@/components/mdx-content";
import { Icon } from "@/components/icon";
import { Button } from "@/components/ui/button";
import { siteConfig } from "@/lib/site";

type Params = Promise<{ slug: string }>;

export function generateStaticParams() {
  return getUseCases().map((u) => ({ slug: u.slug }));
}

export async function generateMetadata({
  params,
}: {
  params: Params;
}): Promise<Metadata> {
  const { slug } = await params;
  const uc = getUseCaseBySlug(slug);
  if (!uc) return {};
  const url = `${siteConfig.url}${uc.url}`;
  return {
    title: uc.title,
    description: uc.description,
    alternates: { canonical: url },
    openGraph: { title: uc.title, description: uc.description, url },
  };
}

const container = "mx-auto max-w-3xl px-4 sm:px-6 lg:px-8";

export default async function UseCasePage({ params }: { params: Params }) {
  const { slug } = await params;
  const uc = getUseCaseBySlug(slug);
  if (!uc) notFound();

  return (
    <div className="relative">
      <div className="pointer-events-none absolute inset-x-0 top-0 -z-10 h-72 glow-accent" />
      <article className={`${container} py-14 sm:py-20`}>
        <Link
          href="/use-cases"
          className="inline-flex items-center gap-1.5 text-sm text-muted transition hover:text-foreground"
        >
          <ArrowLeft className="h-4 w-4" /> All use cases
        </Link>

        <header className="mt-6">
          <div className="flex h-12 w-12 items-center justify-center rounded-xl border border-border bg-accent/10 text-accent">
            <Icon name={uc.icon} className="h-6 w-6" />
          </div>
          <h1 className="mt-5 text-3xl font-semibold tracking-tight sm:text-4xl">
            {uc.title}
          </h1>
          {uc.tagline ? (
            <p className="mt-3 text-lg text-accent">{uc.tagline}</p>
          ) : null}
          <p className="mt-3 text-lg text-muted">{uc.description}</p>
        </header>

        <div className="prose dark:prose-invert mt-10 max-w-none prose-headings:scroll-mt-24 prose-headings:font-semibold prose-headings:tracking-tight prose-a:font-medium prose-a:text-accent prose-a:no-underline hover:prose-a:underline prose-strong:text-foreground">
          <MDXContent code={uc.code} />
        </div>

        <div className="mt-12 flex flex-col gap-3 rounded-xl border border-border bg-surface/60 p-6 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <p className="font-semibold">Ready to try it?</p>
            <p className="text-sm text-muted">
              Install the CLI and run your first command in seconds.
            </p>
          </div>
          <Button href="/docs/getting-started">
            Get started <ArrowRight className="h-4 w-4" />
          </Button>
        </div>
      </article>
    </div>
  );
}
