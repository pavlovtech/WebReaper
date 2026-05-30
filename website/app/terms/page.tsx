import type { Metadata } from "next";
import { LegalPage } from "@/components/legal-page";
import { getPage } from "@/lib/pages";

export function generateMetadata(): Metadata {
  const page = getPage("terms");
  return {
    title: page?.title ?? "Terms",
    description: page?.description,
  };
}

export default function Terms() {
  return <LegalPage slug="terms" />;
}
