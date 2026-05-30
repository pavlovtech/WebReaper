import * as Lucide from "lucide-react";
import type { ComponentType } from "react";

// Namespace import + dynamic lookup: any icon name from content frontmatter
// resolves safely, with a fallback, and no risk of a missing named export.
const icons = Lucide as unknown as Record<
  string,
  ComponentType<{ className?: string }>
>;

export function Icon({
  name,
  className,
}: {
  name: string;
  className?: string;
}) {
  const Cmp = icons[name] ?? Lucide.Sparkles;
  return <Cmp className={className} />;
}
