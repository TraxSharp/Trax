import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "./connection";
import { App } from "./components/App";
import "./index.css";

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
