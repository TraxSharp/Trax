import "./localstorage-polyfill"; // must run before any module that reads localStorage
import "@testing-library/jest-dom/vitest";
import { afterEach } from "vitest";
import { configure } from "@testing-library/dom";
import { configure as configureStorybook } from "storybook/test";
import { setProjectAnnotations } from "@storybook/react-vite";
import preview from "../../.storybook/preview";
import { resetToasts } from "../lib/toast";
import { setPollSeconds } from "../lib/poll";
import { resetActivity } from "../lib/activity";
import { resetOverviewPanels } from "../lib/overviewPanels";
import { resetRefreshStatus } from "../lib/refreshStatus";
import { setHideAdminTrains } from "../lib/adminTrains";
import { clearCredential, setHostAuthenticates } from "../lib/auth";
import { answerConfirm, setAutoConfirm } from "../lib/confirm";

// Reset persisted + module-global state between tests so nothing (theme, a mock overlay delta,
// a lingering toast) leaks across stories.
afterEach(() => {
  try {
    localStorage.clear();
  } catch {
    /* ignore */
  }
  resetToasts();
  setPollSeconds(0); // module-global; reset so an auto-refresh setting can't leak between stories
  resetActivity(); // module-global in-flight counter; reset so it can't leak between stories
  resetOverviewPanels(); // module-global panel visibility; reset so a hidden panel can't leak
  resetRefreshStatus(); // module-global "last refresh failed" mark; reset so an error story can't leak
  setHideAdminTrains(true); // module-global preference; reset so a story that shows admin trains can't leak
  setAutoConfirm(null); // a story that auto-answers must not answer the next one
  answerConfirm(false); // an unanswered dialog must not carry over
  clearCredential(); // held in memory only (lib/auth.ts); reset so one test's credential can't leak
  setHostAuthenticates(false); // module-global; a host-authenticated test must not hide the next one's credential button
  document.documentElement.classList.remove("dark");
});

// The mock resolves queries asynchronously; under the full story suite a query can take ~1s, so
// give findBy*/waitFor generous headroom over the 1000ms default (deterministic, not a sleep).
// Play functions use storybook/test's own testing-library instance, so configure both.
configure({ asyncUtilTimeout: 5000 });
configureStorybook({ asyncUtilTimeout: 5000 });

// Register the global .storybook/preview annotations so composeStories applies the mock
// decorator (urql Provider + router) to portable stories rendered in vitest.
setProjectAnnotations([preview]);

// React Flow (components/Graph.tsx) watches its container's size and reads the viewport's zoom from
// a CSS transform; jsdom has neither API. A no-op observer and an identity matrix are enough for
// it to render the positioned nodes and edges the layout gave it.
if (typeof globalThis.ResizeObserver === "undefined") {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  } as unknown as typeof ResizeObserver;
}
if (typeof globalThis.DOMMatrixReadOnly === "undefined") {
  globalThis.DOMMatrixReadOnly = class {
    m22 = 1;
  } as unknown as typeof DOMMatrixReadOnly;
}
