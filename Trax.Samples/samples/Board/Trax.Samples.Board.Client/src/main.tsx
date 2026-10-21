import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { configureTrax } from "./trax/index.g";
import { App } from "./App";
import "./index.css";

// Sam's demo key, registered by the host in Development only. The page only reads the board; the agents move it.
const KEY = "board-sam-key-do-not-use-in-production";

configureTrax({
  url: "http://localhost:5290/trax/graphql",
  socketUrl: "ws://localhost:5290/trax/graphql",
  headers: () => ({ "X-Api-Key": KEY }),
  connectionParams: () => ({ apiKey: KEY }),
  user: "sam",
});

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
