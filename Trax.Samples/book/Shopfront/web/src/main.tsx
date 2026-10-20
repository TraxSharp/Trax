import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import { signIn } from "./connection";

signIn("ada").then(() =>
  createRoot(document.getElementById("root")!).render(
    <StrictMode>
      <App />
    </StrictMode>,
  ),
);
