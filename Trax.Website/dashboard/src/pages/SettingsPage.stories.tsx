import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { SettingsPage } from "./SettingsPage";
import { schedulerConfigScenario } from "../mock/scenarios";
import { noLogLevelServiceOverlay } from "../mock/store/overlays";

const meta = {
  title: "Pages/Server settings",
  component: SettingsPage,
  parameters: { route: "/settings/server", mock: schedulerConfigScenario },
} satisfies Meta<typeof SettingsPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Mock: Story = {};

export const Real: Story = { parameters: { real: true } };

// Toggle a setting and save the scheduler config.
export const SaveConfig: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "Scheduler settings" })).toBeInTheDocument());
    const toggle = c.getAllByRole("checkbox")[0];
    const before = (toggle as HTMLInputElement).checked;
    await userEvent.click(toggle);
    expect((toggle as HTMLInputElement).checked).toBe(!before);
    await userEvent.click(c.getByRole("button", { name: "Save" }));
    // A success toast confirms the save (previously there was no confirmation).
    expect(await c.findByText(/Scheduler updated|saved/i)).toBeInTheDocument();
  },
};

// The previously read-only duration fields are now editable TimeSpan text inputs; editing one
// and saving round-trips through UpdateScheduler.
export const EditDurationAndSave: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() =>
      expect(c.getByRole("heading", { name: "Scheduler settings" })).toBeInTheDocument(),
    );
    // Durations are a number input + a unit dropdown. Manager poll is seeded PT5S -> 5 + seconds.
    const num = c.getByLabelText("Manifest manager poll") as HTMLInputElement;
    const unit = c.getByLabelText("Manifest manager poll unit") as HTMLSelectElement;
    expect(num).toHaveValue(5);
    expect(unit).toHaveValue("s");
    await userEvent.clear(num);
    await userEvent.type(num, "30");
    await userEvent.selectOptions(unit, "m"); // 30 minutes
    expect(num).toHaveValue(30);
    await userEvent.click(c.getByRole("button", { name: "Save" }));
    expect(await c.findByText(/Scheduler updated|saved/i)).toBeInTheDocument();
  },
};

// The number fields sanitize their input: integer fields strip everything non-numeric, the
// backoff multiplier keeps a decimal point.
export const NumberFieldsSanitizeInput: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "Scheduler settings" })).toBeInTheDocument());
    const [maxRetries, backoff, maxActive] = c.getAllByRole("textbox") as HTMLInputElement[];
    await userEvent.clear(maxRetries);
    await userEvent.type(maxRetries, "1a.2");
    expect(maxRetries).toHaveValue("12"); // integer: dot + letters stripped
    await userEvent.clear(backoff);
    await userEvent.type(backoff, "2x.5y");
    expect(backoff).toHaveValue("2.5"); // decimal: dot kept
    await userEvent.clear(maxActive);
    await userEvent.type(maxActive, "ab30");
    expect(maxActive).toHaveValue("30");
  },
};

// Blanking "Max active jobs" and saving hits the clearMaxActiveJobs branch without erroring;
// the field stays cleared.
export const ClearMaxActiveJobsSaves: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "Scheduler settings" })).toBeInTheDocument());
    const maxActive = c.getAllByRole("textbox")[2] as HTMLInputElement;
    await userEvent.clear(maxActive);
    await userEvent.click(c.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(c.getByRole("heading", { name: "Scheduler settings" })).toBeInTheDocument());
    expect(maxActive).toHaveValue("");
  },
};

// The failure count window is edited like the other durations and saved with them.
export const FailureCountWindow: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByRole("heading", { name: "Scheduler settings" })).toBeInTheDocument());
    const num = c.getByLabelText("Failure count window") as HTMLInputElement;
    const unit = c.getByLabelText("Failure count window unit") as HTMLSelectElement;
    expect(num).toHaveValue(1);
    expect(unit).toHaveValue("d");
    await userEvent.clear(num);
    await userEvent.type(num, "12");
    await userEvent.selectOptions(unit, "h");
    await userEvent.click(c.getByRole("button", { name: "Save" }));
    expect(await c.findByText(/Scheduler updated|saved/i)).toBeInTheDocument();
  },
};

// Each configured category with its configured level and the level in force; a level set at
// runtime is marked as an override.
export const LogLevels: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const table = await c.findByRole("table", { name: "Log levels" });
    const row = within(table).getByText("Trax.Scheduler").closest("tr") as HTMLElement;
    expect(within(row).getAllByRole("cell")[1]).toHaveTextContent("Information"); // configured
    expect(within(row).getByRole("combobox")).toHaveValue("Debug"); // in force
    expect(within(row).getByText("Runtime override")).toBeInTheDocument();
    const aspnet = within(table).getByText("Microsoft.AspNetCore").closest("tr") as HTMLElement;
    expect(within(aspnet).queryByText("Runtime override")).not.toBeInTheDocument();
    expect(c.getByRole("button", { name: "Save log levels" })).toBeDisabled();
  },
};

// Change a level and save: setLogLevels sends only that category, the read shows it in force as
// a runtime override.
export const SaveLogLevel: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const select = await c.findByLabelText("Microsoft.AspNetCore level");
    await userEvent.selectOptions(select, "Error");
    expect(c.getByText("1 unsaved change(s)")).toBeInTheDocument();
    await userEvent.click(c.getByRole("button", { name: "Save log levels" }));
    expect(await c.findByText("1 log level(s) set in this process.")).toBeInTheDocument();
    const row = c.getByText("Microsoft.AspNetCore").closest("tr") as HTMLElement;
    await waitFor(() => expect(within(row).getByText("Runtime override")).toBeInTheDocument());
    expect(within(row).getByRole("combobox")).toHaveValue("Error");
    expect(c.queryByText(/unsaved change/)).not.toBeInTheDocument();
  },
};

// Discard puts every edited level back to the one in force.
export const DiscardLogLevels: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const select = await c.findByLabelText("Default level");
    await userEvent.selectOptions(select, "Trace");
    await userEvent.click(c.getByRole("button", { name: "Discard" }));
    expect(select).toHaveValue("Information");
    expect(c.getByRole("button", { name: "Save log levels" })).toBeDisabled();
  },
};

// A category the host sets itself after Trax keeps its own level: the save says the level is not
// the one in force, in the Blazor page's words.
export const LogLevelNotApplied: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const select = await c.findByLabelText("Microsoft.Hosting.Lifetime level");
    await userEvent.selectOptions(select, "Warning");
    await userEvent.click(c.getByRole("button", { name: "Save log levels" }));
    const alert = await c.findByRole("alert");
    expect(alert).toHaveTextContent(
      "Log levels not applied. The host sets these categories' levels after the dashboard, so the saved level is not the one in force: Microsoft.Hosting.Lifetime.",
    );
  },
};

// A host without AddScheduler registers no log level service: the save is refused with the API's
// reason and the edit stays, so nothing looks saved.
export const LogLevelsRefused: Story = {
  parameters: { overlays: [noLogLevelServiceOverlay] },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const select = await c.findByLabelText("Default level");
    await userEvent.selectOptions(select, "Warning");
    await userEvent.click(c.getByRole("button", { name: "Save log levels" }));
    expect(await c.findByRole("alert")).toHaveTextContent(/Log levels not saved\. This host registers no log level service/);
    expect(select).toHaveValue("Warning");
    expect(c.getByText("1 unsaved change(s)")).toBeInTheDocument();
  },
};

export const NoLogLevels: Story = {
  parameters: {
    mock: {
      resolvers: () => ({
        ConfigQueries: {
          ...(schedulerConfigScenario.resolvers as () => Record<string, Record<string, unknown>>)().ConfigQueries,
          logLevels: () => [],
        },
      }),
    },
  },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText("No log levels are configured.")).toBeInTheDocument();
  },
};
