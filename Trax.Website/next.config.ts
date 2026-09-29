import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  images: {
    remotePatterns: [
      {
        protocol: "https",
        hostname: "img.shields.io",
      },
    ],
  },
  async rewrites() {
    return [
      // /docs/<slug>.md serves the page's raw markdown. The docs pages own the
      // /docs/[...slug] catch-all, so the markdown route lives elsewhere.
      {
        source: "/docs/:slug(.+)\\.md",
        destination: "/docs-markdown/:slug",
      },
    ];
  },
};

export default nextConfig;
