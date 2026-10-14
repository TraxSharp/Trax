import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { PersistedOperationsPage } from "./PersistedOperationsPage";
import {
  errorOverride,
  persistedOperationsScenario,
  persistedOperationsUnavailable,
} from "../mock/scenarios";

const meta = {
  title: "Pages/Persisted ops",
  component: PersistedOperationsPage,
  parameters: { route: "/persisted-operations", mock: persistedOperationsScenario },
} satisfies Meta<typeof PersistedOperationsPage>;

export default meta;
type Story = StoryObj<typeof meta>;

const table = (c: ReturnType<typeof within>) => within(c.getByRole("table"));

export const Mock: Story = {};
export const Real: Story = { parameters: { real: true } };

// Every operation, with its tenant ("(default)" for none), status, version and description.
export const Lists: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(table(c).getByText("greet.v1")).toBeInTheDocument());
    expect(table(c).getByRole("link", { name: "orders.list" })).toHaveAttribute(
      "href",
      "/persisted-operations/orders.list?tenant=acme",
    );
    expect(table(c).getByRole("link", { name: "greet.v1" })).toHaveAttribute("href", "/persisted-operations/greet.v1");
    expect(table(c).getAllByText("(default)")).toHaveLength(2);
    expect(table(c).getByText("Inactive")).toBeInTheDocument();
    expect(c.getByText("1–3 of 3")).toBeInTheDocument();
  },
};

// Status, tenant and id-prefix filters each narrow the list, and compose.
export const Filters: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(table(c).getByText("greet.v1")).toBeInTheDocument());
    await userEvent.selectOptions(c.getByLabelText("Status"), "inactive");
    await waitFor(() => expect(table(c).queryByText("greet.v1")).not.toBeInTheDocument());
    expect(table(c).getByText("greet.v0")).toBeInTheDocument();
    await userEvent.selectOptions(c.getByLabelText("Status"), "all");
    await userEvent.selectOptions(c.getByLabelText("Tenant"), "named");
    await userEvent.type(c.getByLabelText("Tenant key"), "acme");
    await waitFor(() => expect(table(c).queryByText("greet.v1")).not.toBeInTheDocument());
    expect(table(c).getByText("orders.list")).toBeInTheDocument();
    await userEvent.selectOptions(c.getByLabelText("Tenant"), "default");
    expect(c.queryByLabelText("Tenant key")).not.toBeInTheDocument();
    await userEvent.type(c.getByLabelText("Id starts with"), "greet.v1");
    await waitFor(() => expect(table(c).queryByText("greet.v0")).not.toBeInTheDocument());
    expect(table(c).getByText("greet.v1")).toBeInTheDocument();
  },
};

export const FilterYieldsEmpty: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(table(c).getByText("greet.v1")).toBeInTheDocument());
    await userEvent.type(c.getByLabelText("Id starts with"), "zzz");
    expect(await c.findByText("No persisted operations.")).toBeInTheDocument();
    expect(c.getByRole("button", { name: "Next" })).toBeDisabled();
  },
};

// Upload: the editor saves a new operation, which then leads the list.
export const Upload: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(table(c).getByText("greet.v1")).toBeInTheDocument());
    await userEvent.click(c.getByRole("button", { name: "Upload" }));
    await waitFor(() => expect(c.getByText("Upload persisted operation")).toBeInTheDocument());
    const save = c.getByRole("button", { name: "Save" });
    expect(save).toBeDisabled(); // id and document are required
    await userEvent.type(c.getByLabelText("Id"), "status.v1");
    await userEvent.type(c.getByLabelText("Description (optional)"), "Status probe");
    await userEvent.type(c.getByLabelText("Document"), "query Status {{ operations {{ health {{ status } } }");
    await userEvent.click(save);
    await waitFor(() => expect(c.queryByText("Upload persisted operation")).not.toBeInTheDocument());
    expect(await c.findByText("Operation uploaded.")).toBeInTheDocument();
    await waitFor(() => expect(table(c).getByText("status.v1")).toBeInTheDocument());
    expect(table(c).getByText("Status probe")).toBeInTheDocument();
  },
};

// A document that does not parse is refused with the error's title and location; the editor
// stays open.
export const UploadParseError: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(table(c).getByText("greet.v1")).toBeInTheDocument());
    await userEvent.click(c.getByRole("button", { name: "Upload" }));
    await userEvent.type(c.getByLabelText("Id"), "broken.v1");
    await userEvent.type(c.getByLabelText("Document"), "query Broken {{");
    await userEvent.click(c.getByRole("button", { name: "Save" }));
    const alert = await c.findByRole("alert");
    expect(alert).toHaveTextContent("Parse error");
    expect(alert).toHaveTextContent("at line 1, column 15");
    expect(c.getByText("Upload persisted operation")).toBeInTheDocument();
  },
};

// A host without UsePersistedOperations: the page says how to enable it.
export const NotEnabled: Story = {
  parameters: { mock: persistedOperationsUnavailable },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText(/Persisted Operations is not enabled on this server/)).toBeInTheDocument();
    expect(c.queryByRole("button", { name: "Upload" })).not.toBeInTheDocument();
  },
};

export const LoadError: Story = {
  parameters: { mock: errorOverride("PersistedOperationQueries", "persistedOperations") },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText(/Simulated backend error/)).toBeInTheDocument();
  },
};
