import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, expect, test } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { ConsoleClientProvider } from "../lib/consoleClient";
import { AppRoutes } from "../AppRoutes";
import { createMockClient } from "../mock";
import { machineQueryResolvers, machineScenarioWithBoard } from "../mock/scenarios";

afterEach(cleanup);

function open(path: string, withBoard = true) {
  const client = createMockClient({
    fixtures: false,
    overrides: withBoard
      ? machineScenarioWithBoard
      : { resolvers: () => ({ OperationsQueries: { ...machineQueryResolvers, machineBoard: () => null } }) },
  });
  render(
    <ConsoleClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <AppRoutes />
      </MemoryRouter>
    </ConsoleClientProvider>,
  );
}

test("the sidebar lists Board right under Overview", async () => {
  open("/board");

  const nav = await screen.findByRole("navigation", { name: "Console" });
  await within(nav).findByRole("link", { name: "Board" });
  const links = within(nav).getAllByRole("link").map((a) => a.textContent?.trim());
  expect(links.slice(0, 2)).toEqual(["Overview", "Board"]);
});

test("with no machine named, it opens the first machine that has a board", async () => {
  open("/board");

  expect(await screen.findByRole("region", { name: "Board of topic-map" })).toBeInTheDocument();
  const list = screen.getByRole("navigation", { name: "Machines" });
  expect(within(list).getByRole("link", { name: /topic-map/ })).toHaveAttribute("aria-current", "page");
  // The others are listed, and say they have none.
  expect(await within(list).findAllByText("no board")).toHaveLength(2);
});

test("a note lists the scenarios behind it", async () => {
  open("/board/topic-map");

  const board = await screen.findByRole("region", { name: "Board of topic-map" });
  fireEvent.click(await within(board).findByRole("button", { name: "Rebuild, command" }));
  expect(await within(board).findByText("Ada builds it again")).toBeInTheDocument();
  expect(within(board).getByLabelText("topic-map.feature line 14")).toBeInTheDocument();
});

test("a machine with no board says so, and links its states", async () => {
  open("/board/retention-sweep");

  const region = await screen.findByRole("region", { name: "Board of retention-sweep" });
  expect(await within(region).findByText(/is not served from features/)).toBeInTheDocument();
  expect(within(region).getByRole("link", { name: "States and instances" })).toHaveAttribute("href", "/machines/retention-sweep");
});

test("a host with no board at all says how a machine gets one", async () => {
  open("/board", false);

  expect(await screen.findByText(/None of this host's machines is served from features/)).toBeInTheDocument();
});

test("beside the wall, every feature behind the board, and a scenario lights the notes it put there", async () => {
  open("/board/topic-map");

  const panel = await screen.findByRole("complementary", { name: "Scenarios behind it" });
  expect(within(panel).getByText("topic-map.feature")).toBeInTheDocument();
  const rebuildScenario = within(panel).getByRole("button", { name: /Ada builds it again/ });

  fireEvent.click(rebuildScenario);
  expect(rebuildScenario).toHaveAttribute("aria-pressed", "true");
  const wall = screen.getByRole("group", { name: "Board of topic-map" });
  expect(within(wall).getByRole("button", { name: "Rebuild, command" }).className).not.toMatch(/opacity-35/);
  expect(within(wall).getByRole("button", { name: "Build, command" }).className).toMatch(/opacity-35/);
});

test("the heading's help opens how to read the board, not the glossary", async () => {
  open("/board/topic-map");

  const help = await screen.findByRole("link", { name: /^How to read the board/ });
  expect(help).toHaveAttribute("href", "https://traxsharp.net/docs/dashboard/board");
});
