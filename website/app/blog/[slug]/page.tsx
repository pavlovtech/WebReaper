import Link from "next/link";
import { notFound } from "next/navigation";
import type { Metadata } from "next";
import { ArrowLeft } from "lucide-react";
import { getPostBySlug, getPosts, formatDate } from "@/lib/blog";
import { MDXContent } from "@/components/mdx-content";
import { siteConfig } from "@/lib/site";

type Params = Promise<{ slug: string }>;

export function generateStaticParams() {
  return getPosts().map((p) => ({ slug: p.slug }));
}

export async function generateMetadata({
  params,
}: {
  params: Params;
}): Promise<Metadata> {
  const { slug } = await params;
  const post = getPostBySlug(slug);
  if (!post) return {};
  const url = `${siteConfig.url}${post.url}`;
  return {
    title: post.title,
    description: post.description,
    alternates: { canonical: url },
    openGraph: {
      title: post.title,
      description: post.description,
      url,
      type: "article",
      publishedTime: post.date,
      authors: [post.author],
    },
  };
}

const container = "mx-auto max-w-3xl px-4 sm:px-6 lg:px-8";

export default async function BlogPostPage({ params }: { params: Params }) {
  const { slug } = await params;
  const post = getPostBySlug(slug);
  if (!post) notFound();

  return (
    <div className="relative">
      <div className="pointer-events-none absolute inset-x-0 top-0 -z-10 h-72 glow-accent" />
      <article className={`${container} py-14 sm:py-20`}>
        <Link
          href="/blog"
          className="inline-flex items-center gap-1.5 text-sm text-muted transition hover:text-foreground"
        >
          <ArrowLeft className="h-4 w-4" /> Back to blog
        </Link>

        <header className="mt-6 border-b border-border pb-8">
          <div className="flex flex-wrap items-center gap-3 text-sm text-muted-2">
            <span>{formatDate(post.date)}</span>
            <span>·</span>
            <span>{post.author}</span>
            <span>·</span>
            <span>{post.metadata.readingTime} min read</span>
          </div>
          <h1 className="mt-4 text-3xl font-semibold tracking-tight sm:text-4xl">
            {post.title}
          </h1>
          <p className="mt-3 text-lg text-muted">{post.description}</p>
          {post.tags.length > 0 ? (
            <div className="mt-4 flex flex-wrap gap-2">
              {post.tags.map((tag) => (
                <span
                  key={tag}
                  className="rounded-full border border-border bg-surface px-2.5 py-0.5 text-xs text-muted"
                >
                  {tag}
                </span>
              ))}
            </div>
          ) : null}
        </header>

        <div className="prose prose-invert mt-8 max-w-none prose-headings:scroll-mt-24 prose-headings:font-semibold prose-headings:tracking-tight prose-h2:mt-10 prose-a:font-medium prose-a:text-accent prose-a:no-underline hover:prose-a:underline prose-strong:text-foreground prose-li:marker:text-muted-2">
          <MDXContent code={post.code} />
        </div>
      </article>
    </div>
  );
}
