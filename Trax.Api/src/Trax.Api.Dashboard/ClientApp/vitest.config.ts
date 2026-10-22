import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

// Separate from vite.config.ts: tests don't need the Tailwind plugin (class names are just
// strings in jsdom), and keeping the React plugin here is enough to transform TSX + stories.
export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test/setup.ts"],
    include: ["src/**/*.test.{ts,tsx}"],
    testTimeout: 15_000,
    // The e2e suite has its own config (vitest.e2e.config.ts) and needs a live devhost.
    exclude: ["node_modules/**", "dist/**", "src/e2e/**"],
    // The mock's schema comes from graphql-tools and is executed by src/mock/schema-transport.ts.
    // Left external, graphql-tools would load graphql's CommonJS build while the source loads its
    // ES module, and graphql refuses a schema from the other copy; inlined, both load the same one.
    server: { deps: { inline: [/@graphql-tools\//] } },
  },
});
