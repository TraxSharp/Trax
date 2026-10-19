import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, expect, test, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { Provider } from "urql";
import type { Operation } from "@urql/core";
import { AppRoutes } from "../AppRoutes";
import { createMockClient, createMockStore } from "../mock";
import { deadLetterGroupScenario, SHARED_DEAD_LETTER_REASON } from "../mock/scenarios";
import { setAutoConfirm } from "../lib/confirm";

// The Dead letters view groups by train and reason: two manifests of one train that failed the same
// way are one group that names the train, both manifests and the reason, with a count, and opens to
// its rows. Before, they were two rows naming a train and a manifest id each.

afterEach(cleanup);
beforeEach(() => setAutoConfirm(true));
afterEach(() => setAutoConfirm(null));

const GROUP = `ImportFeedTrain, ${SHARED_DEAD_LETTER_REASON}`;

function renderAt(route: string) {
  const client = createMockClient({ store: createMockStore(), fixtures: false, overrides: deadLetterGroupScenario });
  const sent: Operation[] = [];
  const execute = client.executeRequestOperation.bind(client);
  vi.spyOn(client, "executeRequestOperation").mockImplementation((op) => {
    sent.push(op);
    return execute(op);
  });
  render(
    <Provider value={client}>
      <MemoryRouter initialEntries={[route]}>
        <AppRoutes />
      </MemoryRouter>
    </Provider>,
  );
  const named = (name: string) =>
    sent.filter((op) => op.query.definitions.some((d) => d.kind === "OperationDefinition" && d.name?.value === name));
  return { named };
}

const groupRow = async () =>
  (await screen.findByRole("checkbox", { name: `Select group: ${GROUP}` })).closest("tr") as HTMLElement;

test("two dead letters of one train that share a reason are one group naming the train, both manifests and the reason", async () => {
  renderAt("/runs/dead-letters");
  const row = await groupRow();
  const group = within(row);
  expect(group.getByRole("link", { name: "ImportFeedTrain" })).toHaveAttribute(
    "href",
    `/trains/${encodeURIComponent("Trax.Demo.Trains.ImportFeedTrain")}`,
  );
  expect(group.getByText("2 dead letters")).toBeInTheDocument();
  expect(group.getByRole("link", { name: "import-eu" })).toHaveAttribute("href", "/schedules/45");
  expect(group.getByRole("link", { name: "import-us" })).toHaveAttribute("href", "/schedules/46");
  expect(group.getByText(SHARED_DEAD_LETTER_REASON)).toBeInTheDocument();
  // Closed: its rows wait until asked for. The train's other failure is a group of one, shown open.
  expect(screen.queryByRole("checkbox", { name: "Select #721" })).toBeNull();
  expect(screen.getByRole("checkbox", { name: "Select #719" })).toBeInTheDocument();
  expect(screen.getAllByRole("checkbox", { name: /^Select group: / })).toHaveLength(2);
});

test("a group opens to its rows, each linking the dead letter and its manifest", async () => {
  renderAt("/runs/dead-letters");
  await groupRow();
  fireEvent.click(screen.getByRole("button", { name: `Show ${GROUP}` }));
  for (const [id, manifest] of [
    [721, "import-eu"],
    [720, "import-us"],
  ] as const) {
    const row = screen.getByRole("checkbox", { name: `Select #${id}` }).closest("tr") as HTMLElement;
    expect(within(row).getByRole("link", { name: `#${id}` })).toHaveAttribute("href", `/runs/dead-letters/${id}`);
    expect(within(row).getByRole("link", { name: manifest })).toBeInTheDocument();
  }
  fireEvent.click(screen.getByRole("button", { name: `Hide ${GROUP}` }));
  expect(screen.queryByRole("checkbox", { name: "Select #721" })).toBeNull();
});

test("the group's checkbox selects its rows, and Requeue selected sends exactly them", async () => {
  const { named } = renderAt("/runs/dead-letters");
  fireEvent.click(await screen.findByRole("checkbox", { name: `Select group: ${GROUP}` }));
  expect(await screen.findByText("2 selected")).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Requeue selected" }));
  await waitFor(() => expect(named("RequeueDeadLetters")).toHaveLength(1));
  expect(named("RequeueDeadLetters")[0].variables).toEqual({ ids: [721, 720], askAfresh: false });
});

test("the list shows every dead letter as its own row, and the choice is held in the URL", async () => {
  const { named } = renderAt("/runs/dead-letters?view=list");
  const list = await screen.findByRole("table", { name: "Dead letters" });
  await waitFor(() => expect(within(list).getAllByRole("checkbox", { name: /^Select #\d+$/ })).toHaveLength(3));
  expect(within(list).queryByRole("checkbox", { name: /^Select group: / })).toBeNull();
  expect(screen.getByRole("button", { name: "List" })).toHaveAttribute("aria-pressed", "true");
  // The list pages 25 at a time; the grouped read was never sent.
  expect(named("DeadLetters")[0].variables).toMatchObject({ take: 25 });
  expect(named("DeadLetterGroups")).toHaveLength(0);
});

test("the Overview's link to one group shows only that group, open, and offers every group back", async () => {
  const train = "Trax.Demo.Trains.ImportFeedTrain";
  renderAt(
    `/runs/dead-letters?status=AWAITING_INTERVENTION&train=${encodeURIComponent(train)}&reason=${encodeURIComponent(SHARED_DEAD_LETTER_REASON)}`,
  );
  await groupRow();
  expect(screen.getAllByRole("checkbox", { name: /^Select group: / })).toHaveLength(1);
  // Opened, though it holds two.
  expect(await screen.findByRole("checkbox", { name: "Select #721" })).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "Show every group" }));
  await waitFor(() => expect(screen.getAllByRole("checkbox", { name: /^Select group: / })).toHaveLength(2));
});
