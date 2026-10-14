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
