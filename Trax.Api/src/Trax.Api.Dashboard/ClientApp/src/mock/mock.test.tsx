import { render, waitFor } from "@testing-library/react";
import { expect, test } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { Provider } from "urql";
import { AppRoutes } from "../AppRoutes";
import { createMockClient, createMockStore } from "./index";
import { persistedOperationsUnavailable } from "./scenarios";

// Whole-app integration through the mock. Per-page interactions live in the *.stories.tsx play
// functions, exercised headlessly by all-stories.test.tsx.

test("the app landing page (/) boots offline in mock mode", async () => {
  const client = createMockClient({ store: createMockStore() });
  const { findByRole } = render(
    <Provider value={client}>
      <MemoryRouter initialEntries={["/"]}>
        <AppRoutes />
      </MemoryRouter>
    </Provider>,
  );
  // The Overview page heading renders with no backend (metrics from fixtures).
  expect(await findByRole("heading", { name: "Overview" })).toBeInTheDocument();
});

test("fixtures render real captured data on the Work queue page", async () => {
  const client = createMockClient({ store: createMockStore() }); // fixtures on by default
  const { findAllByText } = render(
    <Provider value={client}>
      <MemoryRouter initialEntries={["/work-queue"]}>
        <AppRoutes />
      </MemoryRouter>
    </Provider>,
  );
  // The captured stress dataset uses IStressTrainN names; auto-mock would not produce these.
  const rows = await findAllByText(/IStressTrain/);
  expect(rows.length).toBeGreaterThan(0);
});

test("the sidebar lists Persisted ops when the host exposes the namespace", async () => {
  const client = createMockClient({ store: createMockStore(), fixtures: false });
  const { findByRole, findByTestId } = render(
    <Provider value={client}>
      <MemoryRouter initialEntries={["/trains"]}>
        <AppRoutes />
      </MemoryRouter>
    </Provider>,
  );
  expect(await findByRole("link", { name: /Persisted ops/ })).toBeInTheDocument();
  // The header shows the host's environment.
  expect(await findByTestId("environment-badge")).toBeInTheDocument();
});

test("the sidebar hides Persisted ops on a host without UsePersistedOperations", async () => {
  const client = createMockClient({
    store: createMockStore(),
    fixtures: false,
    overrides: persistedOperationsUnavailable,
  });
  const { findByRole, queryByRole } = render(
    <Provider value={client}>
      <MemoryRouter initialEntries={["/trains"]}>
        <AppRoutes />
      </MemoryRouter>
    </Provider>,
  );
  expect(await findByRole("link", { name: /Work queue/ })).toBeInTheDocument();
  await waitFor(() => expect(queryByRole("link", { name: /Persisted ops/ })).not.toBeInTheDocument());
  // The failed probe is an answer, not a failed refresh.
  expect(queryByRole("status")).not.toBeInTheDocument();
});
