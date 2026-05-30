import type { MetadataRoute } from "next";
import { docs, posts, useCases } from "@/.velite";
import { siteConfig } from "@/lib/site";

export default function sitemap(): MetadataRoute.Sitemap {
  const base = siteConfig.url;

  const staticRoutes: MetadataRoute.Sitemap = [
    "",
    "/docs",
    "/use-cases",
    "/blog",
    "/pricing",
    "/privacy",
    "/terms",
  ].map((p) => ({
    url: `${base}${p}`,
    changeFrequency: "weekly",
    priority: p === "" ? 1 : 0.7,
  }));

  const docRoutes: MetadataRoute.Sitemap = docs
    .filter((d) => d.published !== false)
    .map((d) => ({
      url: `${base}${d.url}`,
      changeFrequency: "weekly",
      priority: 0.6,
    }));

  const useCaseRoutes: MetadataRoute.Sitemap = useCases.map((u) => ({
    url: `${base}${u.url}`,
    changeFrequency: "monthly",
    priority: 0.6,
  }));

  const postRoutes: MetadataRoute.Sitemap = posts
    .filter((p) => !p.draft)
    .map((p) => ({
      url: `${base}${p.url}`,
      lastModified: new Date(p.date),
      changeFrequency: "monthly",
      priority: 0.5,
    }));

  return [...staticRoutes, ...docRoutes, ...useCaseRoutes, ...postRoutes];
}
