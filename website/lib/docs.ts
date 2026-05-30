import { docs } from "@/.velite";

export type Doc = (typeof docs)[number];

const CATEGORY_ORDER = [
  "Introduction",
  "Command line",
  "Library",
  "AI features",
  "Browser & stealth",
  "Scaling",
  "Integrations",
];

export function getPublishedDocs(): Doc[] {
  return docs.filter((d) => d.published !== false);
}

export function getDocBySlug(slug: string): Doc | undefined {
  return getPublishedDocs().find((d) => d.slug === slug);
}

export type DocsNavGroup = {
  category: string;
  items: { title: string; url: string }[];
};

/** Slim, client-safe sidebar tree grouped by category in canonical order. */
export function getDocsNav(): DocsNavGroup[] {
  const byCat = new Map<string, Doc[]>();
  for (const d of getPublishedDocs()) {
    const arr = byCat.get(d.category) ?? [];
    arr.push(d);
    byCat.set(d.category, arr);
  }
  return [...byCat.keys()]
    .sort((a, b) => {
      const ia = CATEGORY_ORDER.indexOf(a);
      const ib = CATEGORY_ORDER.indexOf(b);
      return (ia < 0 ? 999 : ia) - (ib < 0 ? 999 : ib);
    })
    .map((category) => ({
      category,
      items: byCat
        .get(category)!
        .sort((a, b) => a.order - b.order)
        .map((d) => ({ title: d.title, url: d.url })),
    }));
}

/** Flattened doc order, for prev/next navigation. */
export function getFlatDocs(): Doc[] {
  return getDocsNav().flatMap((g) =>
    g.items.map((i) => getPublishedDocs().find((d) => d.url === i.url)!),
  );
}

export function getPager(slug: string) {
  const flat = getFlatDocs();
  const i = flat.findIndex((d) => d.slug === slug);
  return {
    prev: i > 0 ? { title: flat[i - 1].title, url: flat[i - 1].url } : null,
    next:
      i >= 0 && i < flat.length - 1
        ? { title: flat[i + 1].title, url: flat[i + 1].url }
        : null,
  };
}
