import { defineConfig } from "vitest/config";

// E2E suite: hits a real devhost (node env, no jsdom). Run via `npm run test:e2e`, which brings
// up a disposable DB + devhost first. Files run sequentially so the write flows are deterministic.
export default defineConfig({
  test: {
    environment: "node",
    include: ["src/e2e/**/*.e2e.test.ts"],
    fileParallelism: false,
    testTimeout: 30_000,
    hookTimeout: 30_000,
  },
});
