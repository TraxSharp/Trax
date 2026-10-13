import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The state-machine engine, @trax/state-machine, built from its source in this repository. The topic
// map's generated twin imports it, and so do the draft's server and its React hook. The hook's source imports
// react from beside it, so react is deduplicated: the page and the hook share one copy.
const engine = fileURLToPath(new URL("../../../../Trax.Api.StateMachine/src", import.meta.url));

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: [
      { find: /^@trax\/state-machine\/client$/, replacement: `${engine}/client/index.ts` },
      { find: /^@trax\/state-machine\/react$/, replacement: `${engine}/react.ts` },
      { find: /^@trax\/state-machine$/, replacement: `${engine}/index.ts` },
    ],
    dedupe: ["react", "react-dom"],
  },
  // The code panel imports the trains' real C# from the sibling project (`?raw`), and the engine is
  // read from its own folder.
  server: { port: 5173, strictPort: true, fs: { allow: ["..", engine] } },
});
