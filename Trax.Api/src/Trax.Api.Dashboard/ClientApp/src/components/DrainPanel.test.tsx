import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, expect, test, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { ConsoleClientProvider } from "../lib/consoleClient";
import { AppRoutes } from "../AppRoutes";
import { createMockClient, defaultOverlays, type StatefulOverlay } from "../mock";
import { drainBusyScenario, schedulerConfigScenario } from "../mock/scenarios";
import type { MockSchemaOverrides } from "../mock/build-mock-schema";
import type { ConsoleClient } from "../lib/consoleClient";

afterEach(cleanup);

// The whole console at /system/scheduler on the mock host, with every mutation recorded by name.
function open(overrides: MockSchemaOverrides = schedulerConfigScenario, overlays?: StatefulOverlay[], client?: ConsoleClient) {
  const c = client ?? createMockClient({ fixtures: false, overrides, overlays });
  const mutations = vi.spyOn(c, "mutation");
  render(
    <ConsoleClientProvider client={c}>
      <MemoryRouter initialEntries={["/system/scheduler"]}>
        <AppRoutes />
      </MemoryRouter>
    </ConsoleClientProvider>,
  );
  return {
    sent: () => mutations.mock.calls.map(([document, variables]) => [document.name, variables ?? {}]),
  };
}

const panel = async () => within(await screen.findByRole("region", { name: "Drain and resume" }));
const header = () => within(screen.getByRole("banner"));

test("not drained: the panel says so, nothing is busy, and the header shows no mark", async () => {
  open();
  const p = await panel();
  expect(await p.findByText("Not drained: every host starts work as usual.")).toBeInTheDocument();
  expect(p.getByText("Nothing is busy")).toBeInTheDocument();
  expect(p.getByRole("button", { name: "Drain" })).toBeInTheDocument();
  expect(p.queryByRole("button", { name: "Resume" })).not.toBeInTheDocument();
  expect(header().queryByRole("link", { name: "Drained" })).not.toBeInTheDocument();
});

test("the busy list shows each run and instance, and how many more there are", async () => {
  open(drainBusyScenario);
  const p = await panel();
  expect(await p.findByText("Busy: 53 items, the first 3 shown")).toBeInTheDocument();
  const rows = within(p.getByRole("table", { name: "Busy work" })).getAllByRole("row");
  expect(rows).toHaveLength(4);
  expect(within(rows[1]).getByText("4101")).toBeInTheDocument();
  expect(within(rows[1]).getByText("hub-7c9d")).toBeInTheDocument();
  expect(within(rows[3]).getByText("State machine")).toBeInTheDocument();
  expect(within(rows[3]).getByText("Charging, effect Charge in flight")).toBeInTheDocument();
});

test("Drain is confirmed, sends the minutes typed, and the panel and header then say drained", async () => {
  const host = open();
  const p = await panel();
  fireEvent.change(await p.findByLabelText("Drain for (minutes)"), { target: { value: "45" } });
  fireEvent.click(p.getByRole("button", { name: "Drain" }));
  const dialog = await screen.findByRole("dialog", { name: "Drain every host" });
  expect(within(dialog).getByText(/For 45 minutes no host starts work/)).toBeInTheDocument();
  expect(host.sent()).toEqual([]);
  fireEvent.click(within(dialog).getByRole("button", { name: "Drain" }));

  await waitFor(() => expect(host.sent()).toEqual([["DrainHosts", { minutes: 45 }]]));
  expect(await p.findByRole("button", { name: "Resume" })).toBeInTheDocument();
  expect(p.getByTestId("drain-state")).toHaveTextContent(/Drained until .*, set by apikey:admin/);
  await waitFor(() => expect(header().getByRole("link", { name: "Drained" })).toHaveAttribute("href", "/system/scheduler"));
});

test("the default length is 20 minutes", async () => {
  const host = open();
  const p = await panel();
  expect(await p.findByLabelText("Drain for (minutes)")).toHaveValue("20");
  fireEvent.click(p.getByRole("button", { name: "Drain" }));
  const dialog = await screen.findByRole("dialog", { name: "Drain every host" });
  fireEvent.click(within(dialog).getByRole("button", { name: "Drain" }));
  await waitFor(() => expect(host.sent()).toEqual([["DrainHosts", { minutes: 20 }]]));
});

test("a length outside 1 to 1440 minutes is refused here and nothing is sent", async () => {
  const host = open();
  const p = await panel();
  fireEvent.change(await p.findByLabelText("Drain for (minutes)"), { target: { value: "0" } });
  fireEvent.click(p.getByRole("button", { name: "Drain" }));
  expect(await p.findByRole("alert")).toHaveTextContent("A drain lasts from 1 to 1440 minutes.");
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  expect(host.sent()).toEqual([]);
});

test("declining the dialog sends nothing", async () => {
  const host = open();
  const p = await panel();
  fireEvent.click(await p.findByRole("button", { name: "Drain" }));
  const dialog = await screen.findByRole("dialog", { name: "Drain every host" });
  fireEvent.click(within(dialog).getByRole("button", { name: "Keep it" }));
  await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  expect(host.sent()).toEqual([]);
});

test("the host's refusal is shown in its own words, and the panel stays not drained", async () => {
  const refusing: StatefulOverlay = {
    mutations: {
      DrainHosts: () => ({
        operations: { config: { drain: { success: false, count: null, message: "Not drained: who set it must be named in at most 256 characters." } } },
      }),
    },
  };
  open(schedulerConfigScenario, [...defaultOverlays, refusing]);
  const p = await panel();
  fireEvent.click(await p.findByRole("button", { name: "Drain" }));
  fireEvent.click(within(await screen.findByRole("dialog", { name: "Drain every host" })).getByRole("button", { name: "Drain" }));
  expect(await p.findByRole("alert")).toHaveTextContent("Not drained: who set it must be named in at most 256 characters.");
  expect(p.getByRole("button", { name: "Drain" })).toBeInTheDocument();
});

test("Resume is confirmed, sends one mutation, and the header mark goes", async () => {
  const host = open();
  const p = await panel();
  fireEvent.click(await p.findByRole("button", { name: "Drain" }));
  fireEvent.click(within(await screen.findByRole("dialog", { name: "Drain every host" })).getByRole("button", { name: "Drain" }));
  fireEvent.click(await p.findByRole("button", { name: "Resume" }));
  const dialog = await screen.findByRole("dialog", { name: "Resume every host" });
  fireEvent.click(within(dialog).getByRole("button", { name: "Resume" }));

  await waitFor(() => expect(host.sent()).toEqual([["DrainHosts", { minutes: 20 }], ["ResumeHosts", {}]]));
  expect(await p.findByText("Not drained: every host starts work as usual.")).toBeInTheDocument();
  await waitFor(() => expect(header().queryByRole("link", { name: "Drained" })).not.toBeInTheDocument());
});

test("a client that cannot answer the drain read shows no panel and no mark", async () => {
  const client = createMockClient({ fixtures: false, overrides: schedulerConfigScenario });
  client.limitAnswers(["SchedulerConfig", "EnvironmentName", "LogLevels", "ServerVersion"]);
  open(schedulerConfigScenario, undefined, client);
  expect(await screen.findByRole("button", { name: "Pause job dispatcher" })).toBeInTheDocument();
  expect(screen.queryByRole("region", { name: "Drain and resume" })).not.toBeInTheDocument();
  expect(header().queryByRole("link", { name: "Drained" })).not.toBeInTheDocument();
});
