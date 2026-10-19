import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { SettingsPage } from "./SettingsPage";
import { schedulerConfigScenario } from "../mock/scenarios";
import { noLogLevelServiceOverlay } from "../mock/store/overlays";

const meta = {
  title: "Pages/Scheduler",
  component: SettingsPage,
  parameters: { route: "/system/scheduler", mock: schedulerConfigScenario },
} satisfies Meta<typeof SettingsPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Mock: Story = {};

export const Real: Story = { parameters: { real: true } };

const heading = (c: ReturnType<typeof within>) =>
  waitFor(() => expect(c.getByRole("heading", { name: "Scheduler", level: 1 })).toBeInTheDocument());

const section = (c: ReturnType<typeof within>, name: string) => c.getByRole("region", { name });

// Pausing the job dispatcher asks first, in the console's dialog; confirmed, the switch shows
// Paused and offers Resume.
export const PauseDispatcher: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await heading(c);
    await userEvent.click(c.getByRole("button", { name: "Pause job dispatcher" }));
    const dialog = await within(document.body).findByRole("dialog", { name: "Pause job dispatcher" });
    expect(dialog).toHaveTextContent("Queued work waits");
    await userEvent.click(within(dialog).getByRole("button", { name: "Pause job dispatcher" }));
    expect(await c.findByRole("button", { name: "Resume job dispatcher" })).toBeInTheDocument();
    const row = c.getByRole("listitem", { name: "Job dispatcher" });
    expect(within(row).getByText("Paused")).toBeInTheDocument();
    expect(within(c.getByRole("listitem", { name: "Manifest manager" })).getByText("Running")).toBeInTheDocument();
  },
};

// Declining the dialog changes nothing.
export const PauseDeclined: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await heading(c);
    await userEvent.click(c.getByRole("button", { name: "Pause manifest manager" }));
    const dialog = await within(document.body).findByRole("dialog", { name: "Pause manifest manager" });
    await userEvent.click(within(dialog).getByRole("button", { name: "Keep it" }));
    expect(c.getByRole("button", { name: "Pause manifest manager" })).toBeInTheDocument();
  },
};

// A section's Save is enabled only once something in it changed, and saves that section alone.
export const EditDurationAndSave: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await heading(c);
    const polling = within(section(c, "Polling and queue"));
    const save = polling.getByRole("button", { name: "Save polling and queue" });
    expect(save).toBeDisabled();
    // Durations are a number input + a unit dropdown. Manager poll is seeded PT5S -> 5 + seconds.
    const num = polling.getByLabelText("Manifest manager checks schedules every") as HTMLInputElement;
    const unit = polling.getByLabelText("Manifest manager checks schedules every unit") as HTMLSelectElement;
    expect(num).toHaveValue(5);
    expect(unit).toHaveValue("s");
    await userEvent.clear(num);
    await userEvent.type(num, "30");
    await userEvent.selectOptions(unit, "m"); // 30 minutes
    expect(polling.getByText("1 unsaved change(s)")).toBeInTheDocument();
    // Another section is untouched and stays disabled.
    expect(c.getByRole("button", { name: "Save retry" })).toBeDisabled();
    await userEvent.click(save);
    expect(await c.findByText("Scheduler updated")).toBeInTheDocument();
    await waitFor(() =>
      expect(within(section(c, "Polling and queue")).getByLabelText("Manifest manager checks schedules every")).toHaveValue(30),
    );
  },
};

// Discard puts a section back to what is stored.
export const DiscardSection: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await heading(c);
    const retry = within(section(c, "Retry"));
    const num = retry.getByLabelText("Default retry delay") as HTMLInputElement;
    await userEvent.clear(num);
    await userEvent.type(num, "99");
    await userEvent.click(retry.getByRole("button", { name: "Discard" }));
    expect(num).toHaveValue(10);
    expect(retry.getByRole("button", { name: "Save retry" })).toBeDisabled();
  },
};

// The number fields sanitize their input: integer fields strip everything non-numeric, the
// backoff multiplier keeps a decimal point.
export const NumberFieldsSanitizeInput: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await heading(c);
    const maxRetries = c.getByLabelText("Default max retries") as HTMLInputElement;
    const backoff = c.getByLabelText("Retry backoff multiplier") as HTMLInputElement;
    const maxActive = c.getByLabelText("Max active jobs, all groups (blank = no limit)") as HTMLInputElement;
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

// Blanking the scheduler-wide "Max active jobs" and saving sends clearMaxActiveJobs; the field
// stays cleared.
export const ClearMaxActiveJobsSaves: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await heading(c);
    const label = "Max active jobs, all groups (blank = no limit)";
    await userEvent.clear(c.getByLabelText(label));
    await userEvent.click(c.getByRole("button", { name: "Save polling and queue" }));
    expect(await c.findByText("Scheduler updated")).toBeInTheDocument();
    await waitFor(() => expect(c.getByLabelText(label)).toHaveValue(""));
  },
};

// A blank duration is refused in the section before anything is sent.
export const BlankDurationRefused: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await heading(c);
    const timeouts = within(section(c, "Job timeouts"));
    await userEvent.clear(timeouts.getByLabelText("Default job timeout"));
    await userEvent.click(timeouts.getByRole("button", { name: "Save job timeouts" }));
    expect(await timeouts.findByRole("alert")).toHaveTextContent("Default job timeout needs a value.");
  },
};

// The failure count window is edited like the other durations and saved with the retry policy.
export const FailureCountWindow: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await heading(c);
    const num = c.getByLabelText("Failure count window") as HTMLInputElement;
    const unit = c.getByLabelText("Failure count window unit") as HTMLSelectElement;
    expect(num).toHaveValue(1);
    expect(unit).toHaveValue("d");
    await userEvent.clear(num);
    await userEvent.type(num, "12");
    await userEvent.selectOptions(unit, "h");
    await userEvent.click(c.getByRole("button", { name: "Save retry" }));
    expect(await c.findByText("Scheduler updated")).toBeInTheDocument();
  },
};

// Every section says when its change takes effect.
export const EachSectionSaysWhenItApplies: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await heading(c);
    expect(section(c, "Pause and resume")).toHaveTextContent("Takes effect at once");
    expect(section(c, "Polling and queue")).toHaveTextContent("used only when a host next starts");
    expect(section(c, "Retry")).toHaveTextContent("Applies to the next retry");
    expect(section(c, "Job timeouts")).toHaveTextContent("the next time a host starts");
    expect(section(c, "Retention and purge")).toHaveTextContent("next pass");
    expect(section(c, "Log levels")).toHaveTextContent("lasts until it restarts");
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
    await userEvent.click(within(section(c, "Log levels")).getByRole("button", { name: "Discard" }));
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
