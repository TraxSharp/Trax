import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  // The host's CORS policy allows http://localhost:5173 only, unless its PageOrigin names another. strictPort makes a
  // second Vite client fail to start instead of moving to 5174, where every request would be refused.
  server: { port: 5173, strictPort: true },
});
