import type { MetadataRoute } from "next";
import { SITE_URL } from "@/lib/site";

// Everything is public, AI crawlers included: the docs are meant to be read by
// coding agents as well as people.
export default function robots(): MetadataRoute.Robots {
  return {
    rules: { userAgent: "*", allow: "/" },
    sitemap: `${SITE_URL}/sitemap.xml`,
  };
}
