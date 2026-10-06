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
 *
 * Every page may frame this site's own pages (frame-src 'self'), because the
 * policy a browser enforces is the one of the document it first loaded: a
 * client-side navigation to /dashboard keeps the policy of the page it started
 * from, so the dashboard's frame of /dashboard/demo/ must be allowed there too.
 * Being framed stays refused everywhere (frame-ancestors 'none', X-Frame-Options
 * DENY) except the demo, which may be framed only by this site.
 */
function contentSecurityPolicy(frameSrc: string, frameAncestors: string) {
  return [
    "default-src 'self'",
    `script-src 'self' 'unsafe-inline'${isDev ? " 'unsafe-eval'" : ""}`,
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data: https://img.shields.io",
    "font-src 'self'",
    "connect-src 'self'",
    `frame-src ${frameSrc}`,
    `frame-ancestors ${frameAncestors}`,
    "object-src 'none'",
    "base-uri 'self'",
    "form-action 'self'",
  ].join("; ");
}

function securityHeaders(frameSrc = "'self'", frameAncestors = "'none'") {
  return [
    { key: "Content-Security-Policy", value: contentSecurityPolicy(frameSrc, frameAncestors) },
    { key: "X-Content-Type-Options", value: "nosniff" },
    { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
    { key: "X-Frame-Options", value: frameAncestors === "'self'" ? "SAMEORIGIN" : "DENY" },
    {
      key: "Permissions-Policy",
      value: "camera=(), microphone=(), geolocation=(), payment=(), usb=()",
    },
  ];
}

// The dashboard demo is a single-page app: any path under it that is not one
// of its files is a route of the app, answered by its index.html.
const DASHBOARD_DEMO = "/dashboard/demo";

const nextConfig: NextConfig = {
  async headers() {
    // A later entry's header replaces an earlier one's for the paths both match.
    return [
      { source: "/:path*", headers: securityHeaders() },
      { source: `${DASHBOARD_DEMO}/:path*`, headers: securityHeaders("'none'", "'self'") },
      { source: DASHBOARD_DEMO, headers: securityHeaders("'none'", "'self'") },
    ];
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
    return {
      beforeFiles: [],
      afterFiles: [
        // /docs/<slug>.md serves the page's raw markdown. The docs pages own the
        // /docs/[...slug] catch-all, so the markdown route lives elsewhere.
        {
          source: "/docs/:slug(.+)\\.md",
          destination: "/docs-markdown/:slug",
        },
      ],
      // Checked after public files, so the demo's own assets are served as they are.
      fallback: [
        { source: DASHBOARD_DEMO, destination: `${DASHBOARD_DEMO}/index.html` },
        { source: `${DASHBOARD_DEMO}/:path*`, destination: `${DASHBOARD_DEMO}/index.html` },
      ],
    };
  },
};

export default nextConfig;
