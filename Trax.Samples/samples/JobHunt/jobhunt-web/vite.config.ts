import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
    proxy: {
      "/trax": {
        target: "http://localhost:5310",
        changeOrigin: true,
        ws: true,
      },
    },
  },
});
