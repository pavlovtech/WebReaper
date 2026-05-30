import { docs, posts, useCases } from "@/.velite";

export type SearchItem = {
  title: string;
  description: string;
  url: string;
  group: string;
};

/** Slim, client-safe search index (no compiled MDX bodies). */
export function getSearchIndex(): SearchItem[] {
  return [
    ...docs
      .filter((d) => d.published !== false)
      .map((d) => ({
        title: d.title,
        description: d.description ?? "",
        url: d.url,
        group: d.category,
      })),
    ...useCases.map((u) => ({
      title: u.title,
      description: u.description,
      url: u.url,
      group: "Use cases",
    })),
    ...posts
      .filter((p) => !p.draft)
      .map((p) => ({
        title: p.title,
        description: p.description,
        url: p.url,
        group: "Blog",
      })),
  ];
}
