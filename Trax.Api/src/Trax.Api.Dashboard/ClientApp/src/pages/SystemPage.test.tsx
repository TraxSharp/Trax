import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";
import { MemoryRouter, useLocation } from "react-router-dom";
import { Provider } from "urql";
import { AppRoutes } from "../AppRoutes";
import { createMockClient } from "../mock";
import { clusterScenario, schedulerConfigScenario } from "../mock/scenarios";
import type { MockSchemaOverrides } from "../mock/build-mock-schema";

afterEach(cleanup);

function Where() {
  const { pathname, search } = useLocation();
  return <output data-testid="where">{pathname + search}</output>;
}

// The whole console (sidebar, header and the confirm dialog) at `route`, on the mock host, with
// every mutation the page sends recorded.
function open(route: string, overrides: MockSchemaOverrides = schedulerConfigScenario) {
  const client = createMockClient({ fixtures: false, overrides });
  const mutations = vi.spyOn(client, "executeMutation");
  render(
    <Provider value={client}>
      <MemoryRouter initialEntries={[route]}>
        <AppRoutes />
        <Where />
      </MemoryRouter>
    </Provider>,
  );
  return { sent: () => mutations.mock.calls.map(([op]) => op.variables) };
}

test("pausing the job dispatcher is one confirmed mutation, and the header then says dispatch is paused", async () => {
  const host = open("/system/scheduler");
  const header = () => screen.getByRole("banner");
  await screen.findByRole("button", { name: "Pause job dispatcher" });
  expect(within(header()).queryByText("Dispatch paused")).not.toBeInTheDocument();

  fireEvent.click(screen.getByRole("button", { name: "Pause job dispatcher" }));
  const dialog = await screen.findByRole("dialog", { name: "Pause job dispatcher" });
  // Nothing is sent before the operator confirms.
  expect(host.sent()).toEqual([]);
  fireEvent.click(within(dialog).getByRole("button", { name: "Pause job dispatcher" }));

  expect(await screen.findByRole("button", { name: "Resume job dispatcher" })).toBeInTheDocument();
  expect(host.sent()).toEqual([{ input: { jobDispatcherEnabled: false } }]);
  await waitFor(() => expect(within(header()).getByRole("link", { name: "Dispatch paused" })).toBeInTheDocument());
});

test("declining the dialog sends nothing", async () => {
  const host = open("/system/scheduler");
  fireEvent.click(await screen.findByRole("button", { name: "Pause manifest manager" }));
  const dialog = await screen.findByRole("dialog", { name: "Pause manifest manager" });
  fireEvent.click(within(dialog).getByRole("button", { name: "Keep it" }));
  await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  expect(host.sent()).toEqual([]);
  expect(screen.getByRole("button", { name: "Pause manifest manager" })).toBeInTheDocument();
});

test("a section saves only the fields it changed, and no switch", async () => {
  const host = open("/system/scheduler");
  const retry = within(await screen.findByRole("region", { name: "Retry" }));
  fireEvent.change(retry.getByLabelText("Default max retries"), { target: { value: "5" } });
  fireEvent.click(retry.getByRole("button", { name: "Save retry" }));
  await waitFor(() => expect(host.sent()).toEqual([{ input: { defaultMaxRetries: 5 } }]));
});

test("the global and the group limit each say which they are", async () => {
  open("/system/scheduler");
  expect(await screen.findByLabelText("Max active jobs, all groups (blank = no limit)")).toBeInTheDocument();
  expect(screen.getByText(/A manifest group's own limit is set on the group's page/)).toBeInTheDocument();
});

test("the Hosts page warns when no online host runs the job dispatcher, and shows this process", async () => {
  const offline: MockSchemaOverrides = {
    resolvers: () => {
      const base = (clusterScenario.resolvers as () => Record<string, Record<string, () => unknown>>)();
      const hosts = base.OperationsQueries.hosts() as { runsDispatcher: boolean }[];
      return { ...base, OperationsQueries: { hosts: () => hosts.map((h) => ({ ...h, online: !h.runsDispatcher })) } };
    },
  };
  open("/system/hosts", offline);
  expect(await screen.findByRole("heading", { name: "Hosts", level: 1 })).toBeInTheDocument();
  expect(await screen.findByText(/No online host runs the job dispatcher/)).toBeInTheDocument();
  const own = screen.getByRole("region", { name: "This dashboard's process" });
  await waitFor(() => expect(own).toHaveTextContent("Working set"));
  expect(own).toHaveTextContent("GC heap");
  expect(own).toHaveTextContent("Uptime");
});

test("the Logs page keeps its filters in the URL", async () => {
  open("/system/logs?level=WARNING");
  const level = (await screen.findByLabelText("Minimum level")) as HTMLSelectElement;
  expect(level.value).toBe("WARNING");
  fireEvent.change(screen.getByLabelText("Category contains"), { target: { value: "Trax" } });
  await waitFor(() =>
    expect(screen.getByTestId("where")).toHaveTextContent("/system/logs?level=WARNING&category=Trax"),
  );
});

test("Preferences names the browser's own switches apart from the scheduler's", async () => {
  open("/preferences");
  expect(await screen.findByRole("heading", { name: "Preferences", level: 1 })).toBeInTheDocument();
  expect(screen.getByRole("checkbox", { name: "Hide scheduler trains" })).toBeInTheDocument();
  expect(screen.getByLabelText("Refresh this page every (seconds)")).toBeInTheDocument();
  expect(screen.queryByText(/Polling interval/i)).not.toBeInTheDocument();
});
