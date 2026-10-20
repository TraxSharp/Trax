import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// The hub allows requests from http://localhost:5173 only, so the dev server never moves to another port.
export default defineConfig({
  plugins: [react()],
  server: { port: 5173, strictPort: true },
});
