import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { configureTrax } from "./trax/index.g";
import { App } from "./App";
import "./index.css";

// Sam's demo key, registered by the host in Development only. The page only reads the board; the agents move it.
const KEY = "board-sam-key-do-not-use-in-production";

// The host: localhost:5290 in development, or what the build was given. The socket is the same origin over ws or wss.
const API_ORIGIN: string = import.meta.env.VITE_BOARD_API ?? "http://localhost:5290";

configureTrax({
  url: `${API_ORIGIN}/trax/graphql`,
  socketUrl: `${API_ORIGIN.replace(/^http/, "ws")}/trax/graphql`,
  headers: () => ({ "X-Api-Key": KEY }),
  connectionParams: () => ({ apiKey: KEY }),
  user: "sam",
});

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
