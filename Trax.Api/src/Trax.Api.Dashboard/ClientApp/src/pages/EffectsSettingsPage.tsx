import { useState } from "react";
import { useMutation, useQuery } from "urql";
import { EFFECTS } from "../graphql/queries";
import { SET_EFFECT_ENABLED } from "../graphql/mutations";
import { prettyJson, shortName } from "../lib/format";
import { toast } from "../lib/toast";
import { ConfigureEffectDialog } from "../components/ConfigureEffectDialog";
import type { EffectInfo } from "../types";

interface EffectsData {
  operations: { effects: EffectInfo[] };
}

// The registered effects and whether each runs, as the Blazor EffectsSettingsPage shows them: a
// switch per toggleable effect and Enable all / Disable all edit the page, Save sends each changed
// effect to setEffectEnabled, and Discard returns to the server's state. Infrastructure effects
// are always on. A configurable effect's configuration is shown as the API returns it, sensitive
// values already redacted, and Configure edits its settings through configureEffect.
export function EffectsSettingsPage() {
  const [result, reexecute] = useQuery<EffectsData>({ query: EFFECTS });
  const [, setEffectEnabled] = useMutation(SET_EFFECT_ENABLED);
  const effects = result.data?.operations?.effects ?? [];
  // Local edits by fullName; the server's value applies to everything not edited.
  const [edits, setEdits] = useState<Record<string, boolean>>({});
  const [saving, setSaving] = useState(false);
  const [openConfig, setOpenConfig] = useState<string | null>(null);
  const [configuring, setConfiguring] = useState<EffectInfo | null>(null);

  const value = (e: EffectInfo) => edits[e.fullName] ?? e.enabled;
  const changed = effects.filter((e) => e.toggleable && value(e) !== e.enabled);
  const dirty = changed.length > 0;

  function setAll(enabled: boolean) {
    setEdits(Object.fromEntries(effects.filter((e) => e.toggleable).map((e) => [e.fullName, enabled])));
  }

  async function save() {
    setSaving(true);
    const failures: string[] = [];
    for (const e of changed) {
      const r = await setEffectEnabled({ fullName: e.fullName, enabled: value(e) });
      const res = r.data?.operations?.setEffectEnabled;
      if (r.error) failures.push(`${e.name}: ${r.error.message}`);
      else if (!res?.success) failures.push(`${e.name}: ${res?.message ?? "not changed"}`);
    }
    setSaving(false);
    setEdits({});
    reexecute({ requestPolicy: "network-only" });
    if (failures.length > 0) toast(`Some effects were not saved. ${failures.join(" ")}`, "error");
    else toast("Effect settings updated.", "success");
  }

  function discard() {
    setEdits({});
    reexecute({ requestPolicy: "network-only" });
    toast("Changes discarded. The page shows the effects' current state.", "info");
  }

  return (
    <div>
      <h1 className="text-2xl font-bold text-fg mb-2">Effects</h1>
      <p className="text-sm text-muted mb-6">
        Effect changes apply to the next train execution scope in the API process. The registry is
        per-process: a change here does not reach separate scheduler or worker processes.
      </p>

      {result.error && (
        <div className="bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-4 text-sm mb-4">
          {result.error.message}
        </div>
      )}

      {effects.length > 0 && (
        <div className="flex justify-end gap-2 mb-3">
          <button
            onClick={() => setAll(true)}
            disabled={saving}
            className="text-sm px-3 py-1 rounded-md border border-ok-line text-ok-fg hover:bg-ok-soft disabled:opacity-50"
          >
            Enable all
          </button>
          <button
            onClick={() => setAll(false)}
            disabled={saving}
            className="text-sm px-3 py-1 rounded-md border border-danger-line text-danger-fg hover:bg-danger-soft disabled:opacity-50"
          >
            Disable all
          </button>
        </div>
      )}

      <div
        className={`bg-surface rounded-lg border overflow-hidden ${
          dirty ? "border-warn-line" : "border-line"
        }`}
      >
        <table className="w-full text-sm">
          <thead className="bg-sunken text-muted text-left">
            <tr>
              <th className="px-4 py-2 font-medium">Effect</th>
              <th className="px-4 py-2 font-medium">Type</th>
              <th className="px-4 py-2 font-medium">State</th>
              <th className="px-4 py-2 font-medium">Configuration</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {effects.map((e) => (
              <EffectRow
                key={e.fullName}
                effect={e}
                enabled={value(e)}
                edited={e.toggleable && value(e) !== e.enabled}
                configOpen={openConfig === e.fullName}
                onToggle={(on) => setEdits((cur) => ({ ...cur, [e.fullName]: on }))}
                onToggleConfig={() => setOpenConfig((cur) => (cur === e.fullName ? null : e.fullName))}
                onConfigure={() => setConfiguring(e)}
              />
            ))}
            {effects.length === 0 && !result.fetching && (
              <tr>
                <td colSpan={4} className="px-4 py-8 text-center text-muted">
                  No effects registered.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {configuring && (
        <ConfigureEffectDialog
          effect={configuring}
          onClose={() => setConfiguring(null)}
          onSaved={() => reexecute({ requestPolicy: "network-only" })}
        />
      )}

      {effects.length > 0 && (
        <div className="flex items-center justify-end gap-2 mt-4">
          {dirty && (
            <span className="text-xs text-warn-fg mr-auto">
              {changed.length} unsaved change(s)
            </span>
          )}
          <button
            onClick={discard}
            disabled={saving}
            className="px-4 py-2 rounded-lg text-sm border border-line-strong text-fg-2 hover:bg-hover disabled:opacity-50"
          >
            Discard changes
          </button>
          <button
            onClick={save}
            disabled={!dirty || saving}
            className="px-4 py-2 rounded-lg text-sm bg-accent text-on-accent hover:bg-accent-hover disabled:opacity-50"
          >
            {saving ? "Saving…" : "Save"}
          </button>
        </div>
      )}
    </div>
  );
}

function EffectRow({
  effect: e,
  enabled,
  edited,
  configOpen,
  onToggle,
  onToggleConfig,
  onConfigure,
}: {
  effect: EffectInfo;
  enabled: boolean;
  edited: boolean;
  configOpen: boolean;
  onToggle: (on: boolean) => void;
  onToggleConfig: () => void;
  onConfigure: () => void;
}) {
  return (
    <>
      <tr>
        <td className="px-4 py-2 font-medium text-fg">{e.name}</td>
        <td
          className="px-4 py-2 text-muted font-mono text-xs truncate max-w-md"
          title={e.fullName}
        >
          {e.fullName}
        </td>
        <td className="px-4 py-2">
          {!e.toggleable ? (
            <span className="text-xs px-2 py-0.5 rounded-full bg-info-soft text-info-fg">
              Always on
            </span>
          ) : (
            <label className="inline-flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                role="switch"
                aria-label={`${e.name} enabled`}
                checked={enabled}
                onChange={(ev) => onToggle(ev.target.checked)}
              />
              <span
                className={`text-xs px-2 py-0.5 rounded-full ${
                  enabled
                    ? "bg-ok-soft text-ok-fg"
                    : "bg-raised text-muted"
                }`}
              >
                {enabled ? "Enabled" : "Disabled"}
              </span>
              {edited && <span className="text-xs text-warn-fg">edited</span>}
            </label>
          )}
        </td>
        <td className="px-4 py-2">
          {e.isConfigurable ? (
            <span className="inline-flex gap-3">
              <button
                onClick={onToggleConfig}
                aria-expanded={configOpen}
                className="text-xs text-accent-fg hover:underline"
              >
                {configOpen ? "Hide" : "View"}
              </button>
              <button
                onClick={onConfigure}
                aria-label={`Configure ${e.name}`}
                className="text-xs text-accent-fg hover:underline"
              >
                Configure
              </button>
            </span>
          ) : (
            <span className="text-faint">—</span>
          )}
        </td>
      </tr>
      {configOpen && (
        <tr>
          <td colSpan={4} className="px-4 pb-4">
            <p className="text-xs text-muted mb-1">
              {e.configurationTypeName ? shortName(e.configurationTypeName) : "Configuration"}{" "}
              (sensitive values are redacted; Configure edits it)
            </p>
            <pre className="text-xs font-mono whitespace-pre-wrap break-all bg-inset rounded p-3 text-fg">
              {e.configuration ? prettyJson(e.configuration) : "No configuration to show."}
            </pre>
          </td>
        </tr>
      )}
    </>
  );
}
