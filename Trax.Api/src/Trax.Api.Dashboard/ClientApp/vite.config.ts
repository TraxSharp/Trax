import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

// The dashboard talks to whatever host embeds Trax.Api.GraphQL. In dev it proxies
// /trax (HTTP + the subscription WebSocket) to that host, so the browser sees a
// same-origin API and no CORS is involved. Point it at your API with
// TRAX_API_TARGET (defaults to a local Trax sample host).
const target = process.env.TRAX_API_TARGET ?? "http://localhost:5310";

// `--mode demo` builds the recorded demo (src/demo) that traxsharp.net serves under
// /dashboard/demo/; DASHBOARD_DEMO_BASE serves it from somewhere else.
//
// The app build is what Trax.Api.Dashboard ships, served by a host under whatever path it maps.
// Its asset URLs are relative ("./assets/…"), resolved against the <base href> the host writes
// into index.html, so one build works under any path. Nothing is inlined as a data: URL, so the
// host's Content-Security-Policy can allow fonts and scripts from its own origin only.
export default defineConfig(({ mode }) => ({
  plugins: [react(), tailwindcss()],
  base: mode === "demo" ? (process.env.DASHBOARD_DEMO_BASE ?? "/dashboard/demo/") : "./",
  build: mode === "demo" ? { outDir: "dist-demo" } : { assetsInlineLimit: 0 },
  server: {
    port: 5173,
    proxy: {
      "/trax": {
        target,
        changeOrigin: true,
        ws: true,
      },
    },
  },
}));
