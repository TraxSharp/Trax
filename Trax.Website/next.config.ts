import type { NextConfig } from "next";

const isDev = process.env.NODE_ENV === "development";

/**
 * Content-Security-Policy for every route.
 *
 * script-src allows 'unsafe-inline' because Next.js streams each page's RSC
 * payload as inline <script> tags. A per-request nonce would remove that, but
 * it needs a proxy that reads the nonce on every request, which turns every
 * statically generated page dynamic. The policy still refuses scripts from any
 * other origin, plugins, framing and <base> changes. `next dev` additionally
 * needs 'unsafe-eval' for React's development tooling.
 *
 * img.shields.io serves the badges on the landing page; fonts come from
 * next/font, which self-hosts them at build time.
 */
const contentSecurityPolicy = [
  "default-src 'self'",
  `script-src 'self' 'unsafe-inline'${isDev ? " 'unsafe-eval'" : ""}`,
  "style-src 'self' 'unsafe-inline'",
  "img-src 'self' data: https://img.shields.io",
  "font-src 'self'",
  "connect-src 'self'",
  "frame-src 'none'",
  "frame-ancestors 'none'",
  "object-src 'none'",
  "base-uri 'self'",
  "form-action 'self'",
].join("; ");

const securityHeaders = [
  { key: "Content-Security-Policy", value: contentSecurityPolicy },
  { key: "X-Content-Type-Options", value: "nosniff" },
  { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
  { key: "X-Frame-Options", value: "DENY" },
  {
    key: "Permissions-Policy",
    value: "camera=(), microphone=(), geolocation=(), payment=(), usb=()",
  },
];

const nextConfig: NextConfig = {
  async headers() {
    return [{ source: "/:path*", headers: securityHeaders }];
  },
  async redirects() {
    return [
      // Pages renamed in Trax.Docs keep their old URLs working.
      {
        source: "/docs/sdk-reference/configuration/add-service-train-bus",
        destination: "/docs/sdk-reference/configuration/add-mediator",
        permanent: true,
      },
    ];
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
