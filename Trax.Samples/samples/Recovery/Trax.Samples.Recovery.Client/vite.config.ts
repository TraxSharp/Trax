import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  // The code panel imports the trains' real C# from the sibling project (`?raw`).
  server: { port: 5173, strictPort: true, fs: { allow: [".."] } },
});
