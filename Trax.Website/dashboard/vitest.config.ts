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
  },
});
