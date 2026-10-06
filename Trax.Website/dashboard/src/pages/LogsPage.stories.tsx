import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { LogsPage } from "./LogsPage";
import { emptyPage, errorOverride, logsScenario } from "../mock/scenarios";

const meta = {
  title: "Pages/Logs",
  component: LogsPage,
  parameters: { route: "/logs", mock: logsScenario },
} satisfies Meta<typeof LogsPage>;

export default meta;
type Story = StoryObj<typeof meta>;

// Offline, backed by the captured real-data fixtures.
export const Fixtures: Story = { parameters: { fixtures: true, mock: undefined } };

// The same page against the live devhost on :5310 (requires it running).
export const Real: Story = { parameters: { real: true } };

const messages = (c: ReturnType<typeof within>) =>
  within(c.getByRole("table", { name: "Log entries" }))
    .getAllByRole("row")
    .slice(1)
    .map((r) => within(r).getAllByRole("cell")[1].textContent);

// Newest first by default, every run's entries, with the run column.
export const NewestFirst: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("Loading cart")).toBeInTheDocument());
    expect(messages(c)[0]).toBe("Loading cart");
    expect(c.getByRole("columnheader", { name: "Run" })).toBeInTheDocument();
    expect(c.getByText("5 total")).toBeInTheDocument();
  },
};

// Message contains matches anywhere, ignoring case.
export const MessageContains: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("Loading cart")).toBeInTheDocument());
    await userEvent.type(c.getByLabelText("Message contains"), "GATEWAY");
    await waitFor(() => expect(c.queryByText("Loading cart")).not.toBeInTheDocument());
    expect(messages(c)).toEqual(["Gateway timed out after 30sTimeoutException", "Gateway slow, retrying"]);
  },
};

// Category contains and a minimum level compose; a run id narrows to one run.
export const CategoryLevelAndRun: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("Loading cart")).toBeInTheDocument());
    await userEvent.type(c.getByLabelText("Category contains"), "payments");
    await userEvent.selectOptions(c.getByLabelText("Minimum level"), "WARNING");
    await waitFor(() => expect(c.queryByText("Charging card for order 42")).not.toBeInTheDocument());
    expect(c.getByText("2 total")).toBeInTheDocument();
    await userEvent.clear(c.getByLabelText("Category contains"));
    await userEvent.selectOptions(c.getByLabelText("Minimum level"), "");
    await userEvent.type(c.getByLabelText("Run"), "903");
    await waitFor(() => expect(c.getByText("1 total")).toBeInTheDocument());
    expect(c.getByText("Loading cart")).toBeInTheDocument();
  },
};

// Oldest first reads a run's log top to bottom.
export const OldestFirst: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("Loading cart")).toBeInTheDocument());
    await userEvent.selectOptions(c.getByLabelText("Order"), "OLDEST");
    await waitFor(() => expect(messages(c)[0]).toBe("Charging card for order 42"));
    expect(c.getByText(/oldest first/)).toBeInTheDocument();
  },
};

// A text filter past 10,000 matches: the count says "10,000+" and a caption explains the cap.
export const CappedCount: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await userEvent.type(c.getByLabelText("Message contains"), "heartbeat");
    expect(await c.findByText("10,000+ total")).toBeInTheDocument();
    expect(c.getByRole("status")).toHaveTextContent(/More than 10,000 entries match this text filter/);
  },
};

export const NoMatches: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await userEvent.type(c.getByLabelText("Message contains"), "nothing like this");
    expect(await c.findByText("No logs.")).toBeInTheDocument();
  },
};

export const Empty: Story = {
  parameters: { mock: emptyPage("LogQueries", "logs") },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText("No logs.")).toBeInTheDocument();
  },
};

export const LoadError: Story = {
  parameters: { mock: errorOverride("LogQueries", "logs") },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText(/Simulated backend error/)).toBeInTheDocument();
  },
};
