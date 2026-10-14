import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { UserSettingsPage } from "./UserSettingsPage";
import { getOverviewPanels } from "../lib/overviewPanels";

const meta = {
  title: "Pages/User settings",
  component: UserSettingsPage,
  parameters: { route: "/settings/user" },
} satisfies Meta<typeof UserSettingsPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Mock: Story = {};

export const Real: Story = { parameters: { real: true } };

// The theme buttons toggle the `dark` class on <html>.
export const ThemeToggle: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await userEvent.click(c.getByText("Dark"));
    await waitFor(() => expect(document.documentElement.classList.contains("dark")).toBe(true));
    await userEvent.click(c.getByText("Light"));
    await waitFor(() => expect(document.documentElement.classList.contains("dark")).toBe(false));
  },
};

// The auto-refresh control edits the shared polling interval (0 = off).
export const AutoRefresh: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const box = c.getByLabelText("Auto-refresh seconds") as HTMLInputElement;
    expect(box).toHaveValue(0);
    await userEvent.clear(box);
    await userEvent.type(box, "15");
    await waitFor(() => expect(box).toHaveValue(15));
  },
};

// Each Overview panel has a switch that applies at once; Reset default shows every panel again.
export const OverviewPanelToggles: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const failures = c.getByRole("switch", { name: "Show Failures" });
    expect(failures).toBeChecked();
    await userEvent.click(failures);
    await userEvent.click(c.getByRole("switch", { name: "Show Server Health" }));
    expect(failures).not.toBeChecked();
    expect(getOverviewPanels()).toMatchObject({ failures: false, serverHealth: false, summaryCards: true });
    await userEvent.click(c.getByRole("button", { name: "Reset default" }));
    await waitFor(() => expect(c.getByRole("switch", { name: "Show Failures" })).toBeChecked());
    expect(Object.values(getOverviewPanels()).every(Boolean)).toBe(true);
  },
};
