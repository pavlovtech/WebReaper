import { siteConfig } from "@/lib/site";

export function JsonLd() {
  const data = [
    {
      "@context": "https://schema.org",
      "@type": "SoftwareApplication",
      name: "WebReaper",
      applicationCategory: "DeveloperApplication",
      operatingSystem: "Windows, macOS, Linux",
      description: siteConfig.description,
      softwareVersion: siteConfig.version,
      url: siteConfig.url,
      offers: { "@type": "Offer", price: "0", priceCurrency: "USD" },
      license: "https://opensource.org/licenses/MIT",
    },
    {
      "@context": "https://schema.org",
      "@type": "Organization",
      name: "WebReaper",
      url: siteConfig.url,
      sameAs: [siteConfig.links.github, siteConfig.links.nuget],
    },
  ];

  return (
    <script
      type="application/ld+json"
      dangerouslySetInnerHTML={{ __html: JSON.stringify(data) }}
    />
  );
}
