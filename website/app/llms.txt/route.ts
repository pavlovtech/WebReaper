import { docs, posts, useCases } from "@/.velite";
import { siteConfig } from "@/lib/site";

export const dynamic = "force-static";

export function GET() {
  const lines: string[] = [
    "# WebReaper",
    "",
    `> ${siteConfig.description}`,
    "",
    "WebReaper is an AI-native web scraper for .NET. It ships as a single native",
    "binary (CLI) and as a NuGet library. It turns any site into clean Markdown",
    "or structured data, with an optional LLM layer. MIT licensed.",
    "",
    "## Documentation",
    ...docs
      .filter((d) => d.published !== false)
      .map(
        (d) => `- [${d.title}](${siteConfig.url}${d.url}): ${d.description ?? ""}`,
      ),
    "",
    "## Use cases",
    ...useCases.map(
      (u) => `- [${u.title}](${siteConfig.url}${u.url}): ${u.description}`,
    ),
    "",
    "## Blog",
    ...posts
      .filter((p) => !p.draft)
      .map((p) => `- [${p.title}](${siteConfig.url}${p.url}): ${p.description}`),
    "",
    "## Links",
    `- GitHub: ${siteConfig.links.github}`,
    `- NuGet: ${siteConfig.links.nuget}`,
    `- Install (Homebrew): ${siteConfig.install.brew}`,
    "",
  ];

  return new Response(lines.join("\n"), {
    headers: { "Content-Type": "text/plain; charset=utf-8" },
  });
}
