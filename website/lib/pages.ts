import { pages } from "@/.velite";

export type Page = (typeof pages)[number];

export function getPage(slug: string): Page | undefined {
  return pages.find((p) => p.slug === slug);
}
