import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The state-machine engine, @traxsharp/state-machine, built from its source in this repository, as the Recovery page
// does. The generated client imports it.
const engine = fileURLToPath(new URL("../../../../Trax.StateMachine/ts/src", import.meta.url));

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
  server: { port: 5173, strictPort: true, fs: { allow: ["..", engine] } },
});
