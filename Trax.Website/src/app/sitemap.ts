import type { MetadataRoute } from "next";
import { docUrl, getAllDocs } from "@/lib/docs";
import { SITE_URL } from "@/lib/site";

export default function sitemap(): MetadataRoute.Sitemap {
  return [
    { url: SITE_URL, changeFrequency: "weekly", priority: 1 },
    { url: `${SITE_URL}/dashboard`, changeFrequency: "monthly", priority: 0.6 },
    { url: `${SITE_URL}/roadmap`, changeFrequency: "monthly", priority: 0.5 },
    ...getAllDocs()
      .sort((a, b) => a.slug.localeCompare(b.slug))
      .map((doc) => ({
        url: docUrl(doc.slug),
        changeFrequency: "weekly" as const,
        priority: doc.slug === "" ? 0.9 : 0.7,
      })),
  ];
}
