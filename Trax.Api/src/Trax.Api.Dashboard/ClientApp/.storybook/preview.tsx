import "../src/index.css";
import "../src/lib/theme"; // applies persisted/system theme
import { useEffect, useMemo, type ReactNode } from "react";
import type { Decorator, Preview } from "@storybook/react-vite";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { Provider } from "urql";
import {
  createMockClient,
  createRealClient,
  createMockStore,
  defaultOverlays,
  startTrainEventSimulator,
  type MockSchemaOverrides,
  type MockStore,
  type StatefulOverlay,
} from "../src/mock";
import { ToastHost } from "../src/components/ToastHost";
import { createDemoClient } from "../src/demo/client";
import { storyRecordings } from "../src/demo/story-source";

// Every story renders against the in-process GraphQL mock by default (no backend). Shape a
// story's data with `parameters.mock` (a MockSchemaOverrides), or run it against the live
// devhost with `parameters.real = true` — the same story, real data, is the e2e path. A story can
// replace how the mock answers a mutation with `parameters.overlays` (layered over the defaults),
// to stand for a host that answers differently. A test can run every story on the demo's recordings
// instead (src/demo/story-source.ts).
function StoryProviders({
  storyId,
  real,
  overrides,
  overlays,
  fixtures,
  simulate,
  route,
  routePath,
  children,
}: {
  storyId: string;
  real: boolean;
  overrides?: MockSchemaOverrides;
  overlays?: StatefulOverlay[];
  fixtures: boolean;
  simulate: boolean;
  route: string;
  routePath?: string;
  children: ReactNode;
}) {
  const { client, store } = useMemo(() => {
    if (real) return { client: createRealClient(), store: null as MockStore | null };
    // A test can switch every story to the demo's recordings (src/demo/story-source.ts): the same
    // story and play function, answered by what real hosts recorded instead of the auto-mock.
    const recorded = storyRecordings();
    if (recorded) {
      const s = createMockStore({ persist: false, exposeOnWindow: false });
      return {
        client: createDemoClient({ recordings: recorded.recordings, store: s, onServe: recorded.onServe, overlays }),
        store: s,
      };
    }
    // persist: false — otherwise the overlay delta mirrors to localStorage and leaks between
    // stories (a cancel in one story would pre-cancel rows in the next).
    const s = createMockStore({ persist: false });
    // Stories stay deterministic: pin data via `overrides`, not the captured fixtures (which
    // change on re-capture). A story can opt into fixtures with `parameters.fixtures = true`.
    return {
      client: createMockClient({
        overrides,
        store: s,
        fixtures,
        overlays: overlays ? [...defaultOverlays, ...overlays] : undefined,
      }),
      store: s,
    };
    // A fresh client per story keeps overlay writes from leaking between stories.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [storyId, real]);

  // Drive the live feed / notifications from synthetic events — opt-in via `parameters.simulate`
  // so the interval churn doesn't slow the interaction tests that don't need it.
  useEffect(() => {
    if (!store || !simulate) return;
    return startTrainEventSimulator(store, { intervalMs: 1500 });
  }, [store, simulate]);

  // routePath renders the story under a matching <Route> so pages that read useParams()
  // (the detail pages) get their id from `route`.
  const body = routePath ? (
    <Routes>
      <Route path={routePath} element={children} />
    </Routes>
  ) : (
    children
  );

  return (
    <Provider value={client}>
      <MemoryRouter initialEntries={[route]}>
        <div className="min-h-screen bg-inset text-fg p-8">
          {body}
        </div>
        <ToastHost />
      </MemoryRouter>
    </Provider>
  );
}

const withProviders: Decorator = (Story, ctx) => (
  <StoryProviders
    storyId={ctx.id}
    real={Boolean(ctx.parameters.real)}
    overrides={ctx.parameters.mock as MockSchemaOverrides | undefined}
    overlays={ctx.parameters.overlays as StatefulOverlay[] | undefined}
    fixtures={Boolean(ctx.parameters.fixtures)}
    simulate={Boolean(ctx.parameters.simulate)}
    route={(ctx.parameters.route as string | undefined) ?? "/"}
    routePath={ctx.parameters.routePath as string | undefined}
  >
    <Story />
  </StoryProviders>
);

const preview: Preview = {
  decorators: [withProviders],
  parameters: {
    layout: "fullscreen",
    controls: { matchers: { color: /(background|color)$/i, date: /Date$/i } },
  },
};

export default preview;
