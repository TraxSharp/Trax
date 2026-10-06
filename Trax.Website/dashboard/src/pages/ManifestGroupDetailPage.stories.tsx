import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { ManifestGroupDetailPage } from "./ManifestGroupDetailPage";
import { groupDetailScenario } from "../mock/scenarios";

const meta = {
  title: "Pages/Manifest group detail",
  component: ManifestGroupDetailPage,
  parameters: {
    routePath: "/groups/:id",
    route: "/groups/1",
    mock: groupDetailScenario,
  },
} satisfies Meta<typeof ManifestGroupDetailPage>;

export default meta;
type Story = StoryObj<typeof meta>;

function acceptConfirm() {
  const original = window.confirm;
  window.confirm = () => true;
  return () => {
    window.confirm = original;
  };
}

export const Mock: Story = {};

export const Real: Story = { parameters: { real: true, route: "/groups/200" } };

// Edit the group's priority and save.
export const EditAndSave: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "alpha-group" })).toBeInTheDocument());
    const priority = c.getAllByRole("textbox")[1]; // [max active jobs, priority]
    await userEvent.clear(priority);
    await userEvent.type(priority, "7");
    await userEvent.click(c.getByRole("button", { name: "Save" }));
    expect(priority).toHaveValue("7");
  },
};

// The detail page renders group stat cards, the manifests in the group, and recent executions.
export const Richness: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() =>
      expect(c.getByRole("heading", { name: "alpha-group" })).toBeInTheDocument(),
    );
    await waitFor(() => expect(c.getByText("Total runs")).toBeInTheDocument());
    expect(c.getByText("120")).toBeInTheDocument(); // totalExecutions stat
    await waitFor(() => expect(c.getByText("OrderManifest")).toBeInTheDocument()); // manifests-in-group
    await waitFor(() => expect(c.getByText("OrderTrain")).toBeInTheDocument()); // group executions
  },
};

// Max active jobs accepts digits only; priority additionally allows a leading minus.
export const InputsSanitized: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "alpha-group" })).toBeInTheDocument());
    const [maxActive, priority] = c.getAllByRole("textbox") as HTMLInputElement[];
    await userEvent.clear(maxActive);
    await userEvent.type(maxActive, "a1b2");
    expect(maxActive).toHaveValue("12");
    await userEvent.clear(priority);
    await userEvent.type(priority, "x-3y");
    expect(priority).toHaveValue("-3");
  },
};

// A group with no cap renders an empty max-active field with the ∞ placeholder.
export const UnlimitedGroupShowsPlaceholder: Story = {
  parameters: { route: "/groups/2" },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "beta-group" })).toBeInTheDocument());
    const maxActive = c.getAllByRole("textbox")[0] as HTMLInputElement;
    expect(maxActive).toHaveValue("");
    expect(maxActive).toHaveAttribute("placeholder", "∞");
  },
};

// Trigger group confirms and runs without error.
export const TriggerGroup: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByRole("heading", { name: "alpha-group" })).toBeInTheDocument());
      await userEvent.click(c.getByRole("button", { name: "Trigger group" }));
      await waitFor(() => expect(c.getByRole("heading", { name: "alpha-group" })).toBeInTheDocument());
    } finally {
      restore();
    }
  },
};

// Cancel group confirms and runs without error.
export const CancelGroup: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const restore = acceptConfirm();
    try {
      await waitFor(() => expect(c.getByRole("heading", { name: "alpha-group" })).toBeInTheDocument());
      await userEvent.click(c.getByRole("button", { name: "Cancel group" }));
      await waitFor(() => expect(c.getByRole("heading", { name: "alpha-group" })).toBeInTheDocument());
    } finally {
      restore();
    }
  },
};
