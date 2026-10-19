import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, waitFor, within } from "storybook/test";
import { BulkOperationPage, BulkOperationsPage } from "./BulkOperationsPage";
import { BULK_OPERATION_IDS, bulkOperationsOverlay } from "../mock/scenarios";

const meta = {
  title: "Pages/Bulk operations",
  component: BulkOperationsPage,
  parameters: { route: "/system/operations", overlays: [bulkOperationsOverlay] },
} satisfies Meta<typeof BulkOperationsPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Mock: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("link", { name: "Cancel runs" })).toBeInTheDocument());
    expect(c.getByRole("link", { name: "Acknowledge dead letters" })).toBeInTheDocument();
    expect(c.getByRole("link", { name: "Queue manifests" })).toBeInTheDocument();
    expect(c.getByText("Running")).toBeInTheDocument();
    expect(c.getByRole("progressbar", { name: "Progress of Cancel runs" })).toHaveAttribute("aria-valuenow", "192");
    expect(c.getByText("oidc:ops@example.com")).toBeInTheDocument();
  },
};

// The session started none and the host ran none in the last day.
export const Empty: Story = {
  parameters: { overlays: [] },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText(/No bulk operation ran on this host in the last day/)).toBeInTheDocument());
    expect(c.getByRole("link", { name: "How bulk operations work" })).toHaveAttribute(
      "href",
      "https://traxsharp.net/docs/sdk-reference/graphql-api/mutations/bulk-operations",
    );
  },
};

// A running cancel: its counts, the reasons grouped, each failed id linked to its run, and the filter.
export const Running: Story = {
  render: () => <BulkOperationPage />,
  parameters: {
    route: `/system/operations/${BULK_OPERATION_IDS.running}`,
    routePath: "/system/operations/:id",
  },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const counts = within(await c.findByRole("region", { name: "Counts" }));
    expect(within(counts.getByRole("group", { name: "Matched" })).getByText("312")).toBeInTheDocument();
    expect(within(counts.getByRole("group", { name: "Done" })).getByText("180")).toBeInTheDocument();
    expect(within(counts.getByRole("group", { name: "Failed" })).getByText("12")).toBeInTheDocument();
    expect(within(counts.getByRole("group", { name: "Still to do" })).getByText("120")).toBeInTheDocument();
    const reasons = within(c.getByRole("region", { name: "Failure reasons" }));
    expect(reasons.getByText("It had already ended (completed).").nextElementSibling?.textContent).toBe("9");
    const failures = within(c.getByRole("region", { name: "Failures" }));
    expect(failures.getByRole("link", { name: "#88100" })).toHaveAttribute("href", "/runs/88100");
    const filter = within(c.getByRole("region", { name: "Filter" }));
    expect(filter.getByText("Trax.Demo.Trains.ImportTrain")).toBeInTheDocument();
    expect(filter.getByRole("link", { name: "Open the list with this filter" }).getAttribute("href")).toMatch(
      /^\/runs\?train=Trax\.Demo\.Trains\.ImportTrain&state=IN_PROGRESS&after=/,
    );
  },
};

// Stopped on a refused batch: what it reached and what it never attempted.
export const Failed: Story = {
  render: () => <BulkOperationPage />,
  parameters: {
    route: `/system/operations/${BULK_OPERATION_IDS.failed}`,
    routePath: "/system/operations/:id",
  },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const counts = within(await c.findByRole("region", { name: "Counts" }));
    expect(within(counts.getByRole("group", { name: "Not attempted" })).getByText("5")).toBeInTheDocument();
    expect(c.getByText(/the server refused a batch/)).toBeInTheDocument();
    expect(c.getByText(/asking afresh/)).toBeInTheDocument();
    expect(within(c.getByRole("region", { name: "Failures" })).getByRole("link", { name: "#1000001" })).toHaveAttribute(
      "href",
      "/schedules/1000001",
    );
  },
};

// An id this host does not know: it finished over a day ago, or another host ran it.
export const Unknown: Story = {
  render: () => <BulkOperationPage />,
  parameters: {
    route: "/system/operations/99999999-0000-4000-9000-000000000000",
    routePath: "/system/operations/:id",
  },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText(/This host does not know operation/)).toBeInTheDocument());
  },
};
