import {
  defineConfig,
  defineCollection,
  s,
} from "velite";
import rehypeSlug from "rehype-slug";
import rehypeAutolinkHeadings from "rehype-autolink-headings";
import rehypePrettyCode, { type Options as PrettyCodeOptions } from "rehype-pretty-code";
import remarkGfm from "remark-gfm";

const prettyCode: PrettyCodeOptions = {
  theme: "github-dark-default",
  keepBackground: false,
  defaultLang: { block: "text" },
};

const autolink: [typeof rehypeAutolinkHeadings, Record<string, unknown>] = [
  rehypeAutolinkHeadings,
  { behavior: "wrap", properties: { className: ["heading-anchor"] } },
];

const docs = defineCollection({
  name: "Doc",
  pattern: "docs/**/*.mdx",
  schema: s
    .object({
      title: s.string(),
      description: s.string().optional(),
      category: s.string().default("Guides"),
      order: s.number().default(100),
      published: s.boolean().default(true),
      path: s.path(),
      toc: s.toc(),
      metadata: s.metadata(),
      code: s.mdx(),
    })
    .transform((data) => {
      const slug = data.path.replace(/^docs\/?/, "");
      return { ...data, slug, url: `/docs/${slug}` };
    }),
});

const posts = defineCollection({
  name: "Post",
  pattern: "blog/**/*.mdx",
  schema: s
    .object({
      title: s.string(),
      description: s.string(),
      date: s.isodate(),
      author: s.string().default("WebReaper Team"),
      tags: s.array(s.string()).default([]),
      cover: s.image().optional(),
      draft: s.boolean().default(false),
      path: s.path(),
      toc: s.toc(),
      metadata: s.metadata(),
      code: s.mdx(),
    })
    .transform((data) => {
      const slug = data.path.replace(/^blog\/?/, "");
      return { ...data, slug, url: `/blog/${slug}` };
    }),
});

const useCases = defineCollection({
  name: "UseCase",
  pattern: "use-cases/**/*.mdx",
  schema: s
    .object({
      title: s.string(),
      description: s.string(),
      icon: s.string().default("Sparkles"),
      order: s.number().default(100),
      tagline: s.string().optional(),
      path: s.path(),
      code: s.mdx(),
    })
    .transform((data) => {
      const slug = data.path.replace(/^use-cases\/?/, "");
      return { ...data, slug, url: `/use-cases/${slug}` };
    }),
});

const pages = defineCollection({
  name: "Page",
  pattern: "pages/**/*.mdx",
  schema: s
    .object({
      title: s.string(),
      description: s.string().optional(),
      updated: s.isodate().optional(),
      path: s.path(),
      code: s.mdx(),
    })
    .transform((data) => {
      const slug = data.path.replace(/^pages\/?/, "");
      return { ...data, slug, url: `/${slug}` };
    }),
});

export default defineConfig({
  root: "content",
  collections: { docs, posts, useCases, pages },
  mdx: {
    rehypePlugins: [rehypeSlug, [rehypePrettyCode, prettyCode], autolink],
    remarkPlugins: [remarkGfm],
  },
});
