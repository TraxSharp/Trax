import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The state-machine engine, @traxsharp/state-machine, built from its source in this repository. The generated client
// in src/trax imports it. The engine's source imports react from beside it, so react is deduplicated: the page and
// the engine share one copy.
const engine = fileURLToPath(new URL("../../../../Trax.Api.StateMachine/src", import.meta.url));

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: [
      { find: /^@traxsharp\/state-machine\/client$/, replacement: `${engine}/client/index.ts` },
      { find: /^@traxsharp\/state-machine\/react$/, replacement: `${engine}/react.ts` },
      { find: /^@traxsharp\/state-machine$/, replacement: `${engine}/index.ts` },
    ],
    dedupe: ["react", "react-dom"],
  },
  // The host's CORS policy allows http://localhost:5173 only. strictPort makes a second Vite client
  // fail to start instead of moving to 5174, where every request would be refused.
  server: { port: 5173, strictPort: true, fs: { allow: [".", engine] } },
});
