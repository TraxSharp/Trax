import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, test } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { ConsoleClientProvider } from "../lib/consoleClient";
import { AppRoutes } from "../AppRoutes";
import { createMockClient } from "../mock";
import { machineQueryResolvers } from "../mock/scenarios";
import type { Board } from "../lib/machineBoard";

afterEach(cleanup);

const BOARD: Board = {
  machine: "topic-map",
  lanes: ["topic-map"],
  stickies: [
    { id: "command:topic-map:Build", kind: "command", machine: "topic-map", label: "Build", scenarios: [] },
    { id: "aggregate:topic-map", kind: "aggregate", machine: "topic-map", label: "topic-map", scenarios: [] },
  ],
  links: [{ from: "command:topic-map:Build", to: "aggregate:topic-map" }],
};

function open(board: Board | null) {
  const client = createMockClient({
    fixtures: false,
    overrides: {
      resolvers: () => ({ OperationsQueries: { ...machineQueryResolvers, machineBoard: () => board } }),
    },
  });
  render(
    <ConsoleClientProvider client={client}>
      <MemoryRouter initialEntries={["/machines/topic-map"]}>
        <AppRoutes />
      </MemoryRouter>
    </ConsoleClientProvider>,
  );
}

// Each tab names the panel it shows, and that panel is a tabpanel labelled by the tab: a screen reader moves from
// one to the other.
function expectWired(tab: HTMLElement) {
  const panel = document.getElementById(tab.getAttribute("aria-controls") ?? "");
  expect(panel).not.toBeNull();
  expect(panel).toHaveAttribute("role", "tabpanel");
  expect(panel).toHaveAttribute("aria-labelledby", tab.id);
}

test("each tab of a machine with a board controls a panel labelled by it", async () => {
  open(BOARD);

  const states = await screen.findByRole("tab", { name: "State diagram" });
  expectWired(states);

  const board = screen.getByRole("tab", { name: "Board" });
  fireEvent.click(board);
  await screen.findByRole("tabpanel", { name: "Board" });
  expectWired(board);
});

test("a machine with no board has no tabs, and its states are a plain region", async () => {
  open(null);

  const states = await screen.findByRole("region", { name: "States" });
  expect(screen.queryByRole("tablist")).toBeNull();
  expect(states).not.toHaveAttribute("role");
});
