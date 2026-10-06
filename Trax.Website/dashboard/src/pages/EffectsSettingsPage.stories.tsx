import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, userEvent, waitFor, within } from "storybook/test";
import { EffectsSettingsPage } from "./EffectsSettingsPage";
import { effectsScenario } from "../mock/scenarios";

const meta = {
  title: "Pages/Effects settings",
  component: EffectsSettingsPage,
  parameters: { route: "/settings/effects", mock: effectsScenario },
} satisfies Meta<typeof EffectsSettingsPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Mock: Story = {};
export const Real: Story = { parameters: { real: true } };

// Lists effects with the right state badge: infrastructure effects are "Always on", toggleable
// ones show Enabled / Disabled.
export const ListsEffects: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() =>
      expect(c.getByText("JsonEffectProviderFactory")).toBeInTheDocument(),
    );
    expect(c.getByText("Always on")).toBeInTheDocument(); // Postgres factory, not toggleable
    expect(c.getAllByText("Enabled").length).toBeGreaterThan(0);
    expect(c.getByText("Disabled")).toBeInTheDocument(); // Parameter factory disabled
  },
};

export const Empty: Story = {
  parameters: {
    mock: { resolvers: () => ({ OperationsQueries: { effects: () => [] } }) },
  },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("No effects registered.")).toBeInTheDocument());
  },
};

export const LoadError: Story = {
  parameters: {
    mock: {
      resolvers: () => ({
        OperationsQueries: {
          effects: () => {
            throw new Error("Simulated backend error");
          },
        },
      }),
    },
  },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText(/Simulated backend error/)).toBeInTheDocument());
  },
};

// Switching an effect on is a local edit until Save, which sends setEffectEnabled and reads back.
export const ToggleAndSave: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const param = await c.findByRole("switch", { name: "ParameterEffectProviderFactory enabled" });
    expect(param).not.toBeChecked();
    expect(c.getByRole("button", { name: "Save" })).toBeDisabled();
    await userEvent.click(param);
    expect(c.getByText("1 unsaved change(s)")).toBeInTheDocument();
    expect(c.getByText("edited")).toBeInTheDocument();
    await userEvent.click(c.getByRole("button", { name: "Save" }));
    expect(await c.findByText("Effect settings updated.")).toBeInTheDocument();
    await waitFor(() => expect(c.queryByText(/unsaved change/)).not.toBeInTheDocument());
    expect(c.getByRole("switch", { name: "ParameterEffectProviderFactory enabled" })).toBeChecked();
  },
};

// Disable all edits every toggleable effect (not the always-on one); Discard returns to the
// server's state.
export const DisableAllThenDiscard: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await c.findByRole("switch", { name: "JsonEffectProviderFactory enabled" });
    await userEvent.click(c.getByRole("button", { name: "Disable all" }));
    expect(c.getByRole("switch", { name: "JsonEffectProviderFactory enabled" })).not.toBeChecked();
    expect(c.getByText("1 unsaved change(s)")).toBeInTheDocument(); // Parameter was already off
    expect(c.getByText("Always on")).toBeInTheDocument();
    await userEvent.click(c.getByRole("button", { name: "Discard changes" }));
    await waitFor(() =>
      expect(c.getByRole("switch", { name: "JsonEffectProviderFactory enabled" })).toBeChecked(),
    );
    expect(c.queryByText(/unsaved change/)).not.toBeInTheDocument();
  },
};

// Enable all then Save turns every toggleable effect on.
export const EnableAllAndSave: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await c.findByRole("switch", { name: "ParameterEffectProviderFactory enabled" });
    await userEvent.click(c.getByRole("button", { name: "Enable all" }));
    await userEvent.click(c.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(c.queryByText("Disabled")).not.toBeInTheDocument());
    expect(c.getAllByRole("switch").every((s) => (s as HTMLInputElement).checked)).toBe(true);
  },
};

// A configurable effect's configuration is shown as the API redacts it.
export const ViewConfiguration: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await c.findByText("JsonEffectProviderFactory");
    expect(c.getAllByRole("button", { name: "View" })).toHaveLength(1); // only the configurable one
    expect(c.getAllByRole("button", { name: /^Configure / })).toHaveLength(1);
    await userEvent.click(c.getByRole("button", { name: "View" }));
    expect(c.getByText(/"connectionString": "\[REDACTED\]"/)).toBeInTheDocument();
    expect(c.getByText(/JsonEffectConfiguration/)).toBeInTheDocument();
    await userEvent.click(c.getByRole("button", { name: "Hide" }));
    expect(c.queryByText(/connectionString/)).not.toBeInTheDocument();
  },
};

// The open Configure dialog (the modal card holding its title).
async function openConfigure(c: ReturnType<typeof within>) {
  await c.findByText("JsonEffectProviderFactory");
  await userEvent.click(c.getByRole("button", { name: "Configure JsonEffectProviderFactory" }));
  const title = await c.findByText("Configure JsonEffectProviderFactory");
  return within(title.closest("div")!.parentElement as HTMLElement);
}

// The dialog is built from the effect's fields: a switch, a dropdown, a number as text, a sensitive
// setting never shown (blank, "a value is set"), and a setting set in code, read-only.
export const ConfigureFields: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const d = await openConfigure(c);
    expect(d.getByRole("switch", { name: "Write Outputs" })).toBeChecked();
    expect(d.getByRole("combobox", { name: "Log Level" })).toHaveValue("Information");
    expect(d.getByRole("textbox", { name: "Max Bytes" })).toHaveValue("1048576");
    expect(d.getByRole("textbox", { name: "Max Bytes" })).toHaveAttribute("placeholder", "Enter a whole number");
    const secret = d.getByLabelText("Connection String") as HTMLInputElement;
    expect(secret.type).toBe("password");
    expect(secret.value).toBe("");
    expect(secret).toHaveAttribute("placeholder", "Set a new value");
    expect(d.getByText(/a value is set, and it is never shown/)).toBeInTheDocument();
    expect(d.getByTestId("set-in-code-ShouldWrite")).toHaveTextContent("Set in code (not editable here)");
  },
};

// Save sends only what changed; the read shows it (the configuration JSON and the dialog reopened).
export const ConfigureSaves: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    let d = await openConfigure(c);
    await userEvent.clear(d.getByRole("textbox", { name: "Max Bytes" }));
    await userEvent.type(d.getByRole("textbox", { name: "Max Bytes" }), "2048");
    await userEvent.selectOptions(d.getByRole("combobox", { name: "Log Level" }), "Warning");
    await userEvent.click(d.getByRole("button", { name: "Save" }));
    expect(
      await c.findByText("JsonEffectConfiguration updated in this process. Changes apply to the next train execution."),
    ).toBeInTheDocument();
    expect(c.queryByText("Configure JsonEffectProviderFactory")).not.toBeInTheDocument();
    await userEvent.click(c.getByRole("button", { name: "View" }));
    expect(await c.findByText(/"maxBytes": "2048"/)).toBeInTheDocument();
    d = await openConfigure(c);
    await waitFor(() => expect(d.getByRole("textbox", { name: "Max Bytes" })).toHaveValue("2048"));
    expect(d.getByRole("combobox", { name: "Log Level" })).toHaveValue("Warning");
  },
};

// A value the setting's type cannot read is refused beside that setting; nothing is written and the
// dialog stays open.
export const ConfigureRefused: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    const d = await openConfigure(c);
    await userEvent.clear(d.getByRole("textbox", { name: "Max Bytes" }));
    await userEvent.type(d.getByRole("textbox", { name: "Max Bytes" }), "1,000");
    await userEvent.click(d.getByRole("button", { name: "Save" }));
    expect(await d.findByTestId("setting-error-MaxBytes")).toHaveTextContent(
      "'1,000' is not a whole number that fits a Int32, with no thousands separators.",
    );
    expect(d.getByRole("alert")).toHaveTextContent(/^The configuration was not saved: MaxBytes:/);
    expect(c.getByText("Configure JsonEffectProviderFactory")).toBeInTheDocument();
  },
};

// A sensitive setting left blank is not sent; given a value, it is sent, and still never shown.
export const ConfigureSensitive: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    let d = await openConfigure(c);
    await userEvent.type(d.getByLabelText("Connection String"), "Host=new;Password=s3cret");
    await userEvent.click(d.getByRole("button", { name: "Save" }));
    expect(await c.findByText(/JsonEffectConfiguration updated/)).toBeInTheDocument();
    d = await openConfigure(c);
    expect(d.getByLabelText("Connection String")).toHaveValue("");
    expect(c.queryByText(/s3cret/)).not.toBeInTheDocument();
  },
};

// Saving with nothing changed closes without a write; Cancel closes without a write.
export const ConfigureNothingChanged: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    let d = await openConfigure(c);
    await userEvent.click(d.getByRole("button", { name: "Save" }));
    await waitFor(() => expect(c.queryByText("Configure JsonEffectProviderFactory")).not.toBeInTheDocument());
    expect(c.queryByText(/updated in this process/)).not.toBeInTheDocument();
    d = await openConfigure(c);
    await userEvent.click(d.getByRole("switch", { name: "Write Outputs" }));
    await userEvent.click(d.getByRole("button", { name: "Cancel" }));
    expect(c.queryByText(/updated in this process/)).not.toBeInTheDocument();
    d = await openConfigure(c);
    expect(d.getByRole("switch", { name: "Write Outputs" })).toBeChecked();
  },
};
