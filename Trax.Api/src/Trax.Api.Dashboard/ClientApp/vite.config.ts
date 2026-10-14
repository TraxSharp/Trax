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
export default defineConfig(({ mode }) => ({
  plugins: [react(), tailwindcss()],
  base: mode === "demo" ? (process.env.DASHBOARD_DEMO_BASE ?? "/dashboard/demo/") : "/",
  build: mode === "demo" ? { outDir: "dist-demo" } : {},
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
