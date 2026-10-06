import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { DeadLetterDetailPage } from "./DeadLetterDetailPage";
import { deadLetterDetailScenario, errorOverride } from "../mock/scenarios";

const meta = {
  title: "Pages/Dead letter detail",
  component: DeadLetterDetailPage,
  // routePath lets the page read its id from useParams.
  parameters: { routePath: "/dead-letters/:id", route: "/dead-letters/701", mock: deadLetterDetailScenario },
} satisfies Meta<typeof DeadLetterDetailPage>;

export default meta;
type Story = StoryObj<typeof meta>;

function acceptConfirm() {
  const original = window.confirm;
  window.confirm = () => true;
  return () => {
    window.confirm = original;
  };
}

// The captured real-data fixture (route selects a fixtured id).
export const Fixture: Story = { parameters: { fixtures: true, mock: undefined, route: "/dead-letters/1000000" } };
export const Real: Story = { parameters: { real: true } };

// The manifest panel with masked properties, the latest failed run whole, and the failed runs.
export const Panels: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "OrderManifest" })).toBeInTheDocument());
    expect(c.getByText("Dead letter details")).toBeInTheDocument();
    expect(c.getByText("Timeout after 3 attempts")).toBeInTheDocument();
    // Manifest panel: schedule and the masked property.
    expect(await c.findByText(/"apiKey": "\[REDACTED\]"/)).toBeInTheDocument();
    expect(c.getByText("Cron 0 */5 * * * *")).toBeInTheDocument();
    // Most recent failure: run 962 with its stack trace and (masked) input.
    expect(await c.findByText("Most recent failure")).toBeInTheDocument();
    expect(c.getByRole("link", { name: "View run #962" })).toHaveAttribute("href", "/executions/962");
    expect(c.getByText(/at Trax\.Demo\.Trains\.OrderTrain\.ChargeCard\(\)/)).toBeInTheDocument();
    expect(c.getByText(/"cardNumber": "\[REDACTED\]"/)).toBeInTheDocument();
    // Failed execution history.
    expect(await c.findByRole("link", { name: "#961" })).toBeInTheDocument();
    expect(c.getByRole("link", { name: "#962" })).toBeInTheDocument();
  },
};

// Requeue, ask afresh: confirms, re-queues, and toasts the new work queue entry.
export const RequeueAskAfresh: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByRole("button", { name: "Requeue, ask afresh" })).toBeInTheDocument());
      await userEvent.click(c.getByRole("button", { name: "Requeue, ask afresh" }));
      expect(await c.findByText(/has been re-queued \(work queue entry 9000701\)/)).toBeInTheDocument();
    } finally {
      restore();
    }
  },
};

// Acknowledge opens the note dialog; confirming resolves the dead letter and hides its actions.
export const Acknowledge: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("button", { name: "Acknowledge" })).toBeInTheDocument());
    await userEvent.click(c.getByRole("button", { name: "Acknowledge" }));
    await waitFor(() => expect(c.getByText("Acknowledge dead letter #701")).toBeInTheDocument());
    await userEvent.type(c.getByPlaceholderText("Reason / note…"), "known outage");
    const dialog = c.getByText("Acknowledge dead letter #701").closest("div")!.parentElement as HTMLElement;
    await userEvent.click(within(dialog).getByRole("button", { name: "Acknowledge" }));
    await waitFor(() => expect(c.getByText("Status").nextElementSibling).toHaveTextContent("Acknowledged"));
    expect(c.getByText("Resolution note").nextElementSibling).toHaveTextContent("known outage");
    expect(c.queryByRole("button", { name: "Requeue" })).not.toBeInTheDocument();
  },
};

// A resolved dead letter whose manifest has no failed runs: no actions, the empty history.
export const ResolvedWithoutFailedRuns: Story = {
  parameters: { route: "/dead-letters/705" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("Dead letter details")).toBeInTheDocument());
    expect(c.queryByRole("button", { name: "Requeue" })).not.toBeInTheDocument();
    expect(c.queryByRole("button", { name: "Acknowledge" })).not.toBeInTheDocument();
    expect(await c.findByText("No failed executions found for this manifest.")).toBeInTheDocument();
    expect(c.queryByText("Most recent failure")).not.toBeInTheDocument();
  },
};

export const NotFound: Story = {
  parameters: { route: "/dead-letters/9999" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText("No dead letter found.")).toBeInTheDocument();
  },
};

export const LoadError: Story = {
  parameters: { mock: errorOverride("DeadLetterQueries", "deadLetter") },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText(/Simulated backend error/)).toBeInTheDocument();
  },
};

// The manifest row links the manifest's other dead letters.
export const LinksManifestDeadLetters: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByRole("link", { name: "its dead letters" })).toHaveAttribute(
      "href",
      "/dead-letters?manifestId=801",
    );
  },
};
