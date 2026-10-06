import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { Provider } from "urql";
import type { Client } from "@urql/core";
import "./lib/theme"; // applies persisted/system theme before first paint
import { ConnectionGate } from "./components/ConnectionGate";
import { AppRoutes } from "./AppRoutes";
import "./index.css";

const root = createRoot(document.getElementById("root")!);

// Dev-only offline mode: `?mock` (or VITE_MOCK=1) swaps the real client for the in-process
// GraphQL mock and starts the synthetic event simulator. The mock and its heavy deps
// (graphql-tools, the embedded SDL) live behind a dynamic import, so a production build never
// pulls them into the main bundle.
async function boot() {
  // The demo build answers from recordings of real hosts, with no API and no credential (src/demo).
  // The test is a build-time literal, so the normal build drops this branch and the recordings.
  if (import.meta.env.VITE_DEMO === "1") {
    const { bootDemo } = await import("./demo/boot");
    await bootDemo(root);
    return;
  }

  const useMock =
    import.meta.env.DEV &&
    (import.meta.env.VITE_MOCK === "1" ||
      new URLSearchParams(window.location.search).has("mock"));

  if (useMock) {
    const { createMockClient, createMockStore, startTrainEventSimulator } = await import("./mock");
    const store = createMockStore();
    // Fixtures (captured real data) are on by default, so every page renders realistically.
    const client: Client = createMockClient({ store });
    startTrainEventSimulator(store, { intervalMs: 2500 });
    root.render(
      <StrictMode>
        <Provider value={client}>
          <BrowserRouter>
            <AppRoutes />
          </BrowserRouter>
        </Provider>
      </StrictMode>,
    );
    return;
  }

  const { client } = await import("./lib/graphql");
  root.render(
    <StrictMode>
      <Provider value={client}>
        <ConnectionGate>
          <BrowserRouter>
            <AppRoutes />
          </BrowserRouter>
        </ConnectionGate>
      </Provider>
    </StrictMode>,
  );
}

void boot();
