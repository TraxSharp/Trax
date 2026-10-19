import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { Provider } from "urql";
import type { Client } from "@urql/core";
import "./lib/theme"; // applies persisted/system theme before first paint
import { ConnectionGate } from "./components/ConnectionGate";
import { removeStoredCredentials, setHostAuthenticates } from "./lib/auth";
import { AppRoutes } from "./AppRoutes";
import { readRuntimeConfig, routerBasename } from "./lib/runtimeConfig";
import "./index.css";

const root = createRoot(document.getElementById("root")!);

// Dev-only offline mode: `?mock` (or VITE_MOCK=1) swaps the real client for the in-process
// GraphQL mock and starts the synthetic event simulator. The mock and its heavy deps
// (graphql-tools, the embedded SDL) live behind a dynamic import, so a production build never
// pulls them into the main bundle.
async function boot() {
  // The credential is held in memory only (lib/auth.ts); delete any an earlier version stored.
  removeStoredCredentials();

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
    const { createMockClient, createMockStore, devMockSeed, startTrainEventSimulator } = await import("./mock");
    const store = createMockStore();
    // Fixtures (captured real data) are on by default, so every page renders realistically; the
    // seed answers what the fixtures do not hold (run graphs, state machines).
    const client: Client = createMockClient({ store, overrides: devMockSeed });
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
  // The path the host serves the app under (Trax.Api.Dashboard writes it into index.html).
  const config = readRuntimeConfig();
  const basename = routerBasename(config);
  const app = (
    <BrowserRouter basename={basename}>
      <AppRoutes />
    </BrowserRouter>
  );
  // A host that signs the browser in itself needs no key from the operator (HostAuthenticates()).
  setHostAuthenticates(config.hostAuthenticates);
  root.render(
    <StrictMode>
      <Provider value={client}>{config.hostAuthenticates ? app : <ConnectionGate>{app}</ConnectionGate>}</Provider>
    </StrictMode>,
  );
}

void boot();
