import { StrictMode } from "react";
import type { Root } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { Provider } from "urql";
import { AppRoutes } from "../AppRoutes";
import { setConnectionStatus } from "../lib/connection";
import { setFailureAlertLimit } from "../lib/notify";
import { setToastLimit } from "../lib/toast";
import { createMockStore } from "../mock/store/mock-store";
import { setOverlayClock } from "../mock/store/overlays";
import { createDemoClient } from "./client";
import { prepareRecordings, type RecordingsFile } from "./recordings";
import { startReplay } from "./replay";
import recordingsUrl from "./data/dashboard-recordings.json?url";

/**
 * The demo build (`vite build --mode demo`): the whole dashboard, answered by recordings of real
 * Trax hosts instead of an API. No credential is asked for; writes change only this tab, through
 * the mock's overlays, and are gone on reload.
 */
export async function bootDemo(root: Root): Promise<void> {
  const response = await fetch(recordingsUrl);
  const recordings = prepareRecordings((await response.json()) as RecordingsFile);
  // Session-only: nothing persists, and no mock store is exposed, so no mock tooling shows.
  const store = createMockStore({ persist: false, exposeOnWindow: false });
  setOverlayClock(() => new Date().toISOString());
  const client = createDemoClient({ recordings, store });
  // The replay fails the same runs on every loop: the first two failures say so, the rest only show
  // in the tables, and never more than two toasts stand at once.
  setFailureAlertLimit(2);
  setToastLimit(2);
  startReplay(store, recordings);
  // The replay is the live feed, so the socket indicator reads live.
  setConnectionStatus("connected");
  root.render(
    <StrictMode>
      <Provider value={client}>
        <BrowserRouter basename={import.meta.env.BASE_URL.replace(/\/$/, "")}>
          <AppRoutes />
        </BrowserRouter>
      </Provider>
    </StrictMode>,
  );
}
