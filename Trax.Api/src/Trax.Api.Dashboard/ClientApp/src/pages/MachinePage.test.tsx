import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, expect, test } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { ConsoleClientProvider } from "../lib/consoleClient";
import { AppRoutes } from "../AppRoutes";
import { createMockClient } from "../mock";
import { MACHINE_BOARD, machineQueryResolvers } from "../mock/scenarios";
import type { Board } from "../lib/machineBoard";

afterEach(cleanup);

function openAt(path: string, board: Board | null) {
  const client = createMockClient({
    fixtures: false,
    overrides: {
      resolvers: () => ({ OperationsQueries: { ...machineQueryResolvers, machineBoard: () => board } }),
    },
  });
  render(
    <ConsoleClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <AppRoutes />
      </MemoryRouter>
    </ConsoleClientProvider>,
  );
}

const open = (board: Board | null) => openAt("/machines/topic-map", board);

test("a machine with a board links it from its states, and its states are a plain region", async () => {
  open(MACHINE_BOARD);

  const states = await screen.findByRole("region", { name: "States" });
  expect(await screen.findByRole("link", { name: "Its board" })).toHaveAttribute("href", "/board/topic-map");
  expect(screen.queryByRole("tablist")).toBeNull();
  expect(states).not.toHaveAttribute("role");
});

test("a machine with no board has no link to one", async () => {
  open(null);

  await screen.findByRole("region", { name: "States" });
  expect(screen.queryByRole("link", { name: "Its board" })).toBeNull();
});

test("the address of the board tab it used to have opens the Board page", async () => {
  openAt("/machines/topic-map?tab=board", MACHINE_BOARD);

  expect(await screen.findByRole("heading", { name: "Board", level: 1 })).toBeInTheDocument();
  expect(await screen.findByRole("region", { name: "Board of topic-map" })).toBeInTheDocument();
});
