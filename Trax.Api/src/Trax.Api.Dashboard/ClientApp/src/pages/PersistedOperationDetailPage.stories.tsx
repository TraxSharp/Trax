import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { PersistedOperationDetailPage } from "./PersistedOperationDetailPage";
import { persistedOperationsScenario, persistedOperationsUnavailable } from "../mock/scenarios";

const meta = {
  title: "Pages/Persisted op detail",
  component: PersistedOperationDetailPage,
  parameters: {
    routePath: "/persisted-operations/:id",
    route: "/persisted-operations/greet.v1",
    mock: persistedOperationsScenario,
  },
} satisfies Meta<typeof PersistedOperationDetailPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Mock: Story = {};

// The operation's fields, its document, and its history.
export const Detail: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("Greet (v1)")).toBeInTheDocument());
    expect(c.getByText("Shape fingerprint").nextElementSibling).toHaveTextContent("aa11aa11");
    expect(c.getByText("Description").nextElementSibling).toHaveTextContent("Health probe");
    expect(c.getByText("query Greet { operations { health { status } } }")).toBeInTheDocument();
    const history = c.getByRole("table", { name: "History" });
    expect(within(history).getByText("Upsert")).toBeInTheDocument();
    expect(within(history).getByText("aa11aa11aa11…")).toBeInTheDocument();
    expect(c.queryByText("Deprecation reason")).not.toBeInTheDocument();
  },
};

// Deactivate asks for a reason (required), then shows the operation inactive with Restore.
export const DeactivateWithReason: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("button", { name: "Deactivate" })).toBeInTheDocument());
    await userEvent.click(c.getByRole("button", { name: "Deactivate" }));
    await waitFor(() => expect(c.getByText("Deactivate persisted operation")).toBeInTheDocument());
    const dialog = c.getByText("Deactivate persisted operation").closest("div")!.parentElement as HTMLElement;
    const confirm = within(dialog).getByRole("button", { name: "Deactivate" });
    expect(confirm).toBeDisabled();
    await userEvent.type(within(dialog).getByLabelText("Reason"), "   ");
    expect(confirm).toBeDisabled();
    await userEvent.type(within(dialog).getByLabelText("Reason"), "superseded by greet.v2");
    await userEvent.click(confirm);
    await waitFor(() => expect(c.getByRole("button", { name: "Restore" })).toBeInTheDocument());
    expect(c.getByText("Deprecation reason").nextElementSibling).toHaveTextContent("superseded by greet.v2");
    expect(within(c.getByRole("table", { name: "History" })).getByText("Deactivate")).toBeInTheDocument();
    expect(await c.findByText(/Deactivated: greet.v1 \(\(default\)\)/)).toBeInTheDocument();
  },
};

// Cancelling the reason dialog changes nothing.
export const DeactivateCancelled: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("button", { name: "Deactivate" })).toBeInTheDocument());
    await userEvent.click(c.getByRole("button", { name: "Deactivate" }));
    await userEvent.click(c.getByRole("button", { name: "Cancel" }));
    await waitFor(() => expect(c.queryByText("Deactivate persisted operation")).not.toBeInTheDocument());
    expect(c.getByText("Active").nextElementSibling).toHaveTextContent("Yes");
  },
};

// An inactive operation shows why, and Restore brings it back.
export const Restore: Story = {
  parameters: { route: "/persisted-operations/greet.v0" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("Deprecation reason")).toBeInTheDocument());
    expect(c.getByText("Superseded by greet.v1")).toBeInTheDocument();
    await userEvent.click(c.getByRole("button", { name: "Restore" }));
    await waitFor(() => expect(c.getByRole("button", { name: "Deactivate" })).toBeInTheDocument());
    expect(c.getByText("Active").nextElementSibling).toHaveTextContent("Yes");
  },
};

// Editing: a shape-changing edit is refused with both fingerprints until the guardrail is
// bypassed, which shows its warning and then saves.
export const EditShapeDiff: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const edit = async () => {
      await userEvent.click(c.getByRole("button", { name: "Edit" }));
      await waitFor(() => expect(c.getByText("Edit greet.v1")).toBeInTheDocument());
    };
    await waitFor(() => expect(c.getByRole("button", { name: "Edit" })).toBeInTheDocument());
    // First save records the document this session edits against.
    await edit();
    await userEvent.click(c.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(c.queryByText("Edit greet.v1")).not.toBeInTheDocument());
    // Change the selection set: refused.
    await edit();
    const doc = c.getByLabelText("Document");
    await userEvent.clear(doc);
    await userEvent.type(doc, "query Greet {{ operations {{ health {{ status queueDepth } } }");
    await userEvent.click(c.getByRole("button", { name: "Save" }));
    const alert = await c.findByRole("alert");
    expect(alert).toHaveTextContent("Shape change rejected");
    expect(alert).toHaveTextContent(/Old fingerprint: \w{16}…/);
    expect(alert).toHaveTextContent(/New fingerprint: \w{16}…/);
    // Bypass the guardrail: warned, then saved.
    await userEvent.click(c.getByLabelText("Bypass shape-diff guardrail"));
    expect(c.getByText(/Bypass only when the shape change is known to be safe/)).toBeInTheDocument();
    await userEvent.click(c.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(c.queryByText("Edit greet.v1")).not.toBeInTheDocument());
    await waitFor(() => expect(c.getByText(/status queueDepth/)).toBeInTheDocument());
  },
};

// A named tenant's operation is read with its tenant.
export const NamedTenant: Story = {
  parameters: { route: "/persisted-operations/orders.list?tenant=acme" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("Orders (v3)")).toBeInTheDocument());
    expect(c.getByTitle("Tenant")).toHaveTextContent("acme");
  },
};

export const NotFound: Story = {
  parameters: { route: "/persisted-operations/missing.v1" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(
      await c.findByText("Persisted operation 'missing.v1' not found for tenant (default)."),
    ).toBeInTheDocument();
  },
};

export const NotEnabled: Story = {
  parameters: { mock: persistedOperationsUnavailable },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText(/Persisted Operations is not enabled on this server/)).toBeInTheDocument();
  },
};
