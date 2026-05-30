import type { Metadata } from "next";
import { LegalPage } from "@/components/legal-page";
import { getPage } from "@/lib/pages";

export function generateMetadata(): Metadata {
  const page = getPage("privacy");
  return {
    title: page?.title ?? "Privacy",
    description: page?.description,
  };
}

export default function Privacy() {
  return <LegalPage slug="privacy" />;
}
