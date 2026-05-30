import { getDocsNav } from "@/lib/docs";
import { DocsSidebar } from "@/components/docs/sidebar";

export default function DocsLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  const nav = getDocsNav();

  return (
    <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
      <div className="lg:grid lg:grid-cols-[240px_minmax(0,1fr)] lg:gap-10">
        <DocsSidebar nav={nav} />
        <div className="min-w-0">{children}</div>
      </div>
    </div>
  );
}
