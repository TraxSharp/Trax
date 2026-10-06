import { useState } from "react";
import { useMutation, useQuery } from "urql";
import { LOG_LEVELS, SCHEDULER_CONFIG } from "../graphql/queries";
import { SET_LOG_LEVELS, UPDATE_SCHEDULER } from "../graphql/mutations";
import { toast } from "../lib/toast";
import { changedLogLevels, LOG_LEVEL_NAMES, notAppliedMessage } from "../lib/logLevels";
import {
  buildDuration,
  parseDuration,
  UNIT_LABEL,
  type DurationUnit,
} from "../lib/duration";
import type { LogLevelSetting, SchedulerConfigSnapshot, SetLogLevelsResponse } from "../types";

interface ConfigData {
  operations: { config: { scheduler: SchedulerConfigSnapshot } };
}

export function SettingsPage() {
  const [result, reexecute] = useQuery<ConfigData>({ query: SCHEDULER_CONFIG });
  const [, update] = useMutation(UPDATE_SCHEDULER);
  const cfg = result.data?.operations?.config?.scheduler;

  if (result.error)
    return (
      <div className="bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-4 text-sm">
        {result.error.message}
      </div>
    );
  if (!cfg) return <p className="text-muted">Loading…</p>;

  async function save(input: Record<string, unknown>) {
    const r = await update({ input });
    if (r.error) {
      toast(r.error.message, "error");
      return;
    }
    const res = r.data?.operations?.config?.updateScheduler;
    if (res?.success) {
      toast(res.message || "Scheduler settings saved.", "success");
      reexecute({ requestPolicy: "network-only" });
    } else {
      toast(res?.message || "Save failed.", "error");
    }
  }

  return (
    <>
      <SettingsView cfg={cfg} onSave={save} />
      <LogLevelsPanel />
    </>
  );
}

// The level each configured log category filters at in the API process, editable through
// setLogLevels as on the Blazor server settings: a level per category, Save sends the ones changed,
// Discard returns to the levels in force. A change lasts until the API process restarts and does
// not reach the scheduler or worker processes. A host without AddScheduler cannot change them, and
// says so when asked.
function LogLevelsPanel() {
  const [{ data, error }, reexecute] = useQuery<{ operations: { config: { logLevels: LogLevelSetting[] } } }>({
    query: LOG_LEVELS,
  });
  const [, setLogLevels] = useMutation(SET_LOG_LEVELS);
  const levels = data?.operations?.config?.logLevels;
  const [edits, setEdits] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const changed = changedLogLevels(levels ?? [], edits);

  async function save() {
    setProblem(null);
    if (changed.length === 0) return;
    setBusy(true);
    const r = await setLogLevels({ levels: changed });
    setBusy(false);
    const res: SetLogLevelsResponse | undefined = r.data?.operations?.config?.setLogLevels;
    if (r.error || !res) {
      setProblem(r.error?.message ?? "Log levels not saved.");
      return;
    }
    if (!res.success) {
      setProblem(`Log levels not saved. ${res.message}`);
      return;
    }
    setEdits({});
    reexecute({ requestPolicy: "network-only" });
    if (res.notApplied.length > 0) {
      setProblem(`Log levels not applied. ${notAppliedMessage(res.notApplied)}`);
      return;
    }
    toast(res.message || "Log levels updated.", "success");
  }

  function discard() {
    setEdits({});
    setProblem(null);
  }

  return (
    <div
      className={`mt-6 bg-surface rounded-lg border p-5 ${
        changed.length > 0 ? "border-warn-line" : "border-line"
      }`}
    >
      <h2 className="text-sm font-semibold text-fg mb-1">Logging</h2>
      <p className="text-xs text-muted mb-4">
        Log level changes take effect immediately for this process&apos;s loggers. They are runtime overrides: they
        apply only to the process serving the API and reset when it restarts.
      </p>
      {error && <p className="text-sm text-danger-fg">{error.message}</p>}
      {levels && levels.length === 0 && (
        <p className="text-sm text-muted">No log levels are configured.</p>
      )}
      {levels && levels.length > 0 && (
        <table className="w-full text-sm" aria-label="Log levels">
          <thead className="text-left text-muted">
            <tr>
              <th className="py-1 font-medium">Category</th>
              <th className="py-1 font-medium">Configured</th>
              <th className="py-1 font-medium">Level</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {levels.map((l) => (
              <tr key={l.category}>
                <td className="py-1 font-mono text-xs text-fg">{l.category}</td>
                <td className="py-1 text-fg-2">{l.configuredLevel ?? "—"}</td>
                <td className="py-1">
                  <select
                    aria-label={`${l.category} level`}
                    value={edits[l.category] ?? l.level}
                    onChange={(e) => setEdits((cur) => ({ ...cur, [l.category]: e.target.value }))}
                    className="border border-line-strong rounded-md px-2 py-0.5 bg-field"
                  >
                    {/* A level read back that is not one of the names (never expected) still shows. */}
                    {!LOG_LEVEL_NAMES.includes(l.level as (typeof LOG_LEVEL_NAMES)[number]) && (
                      <option value={l.level}>{l.level}</option>
                    )}
                    {LOG_LEVEL_NAMES.map((n) => (
                      <option key={n} value={n}>
                        {n}
                      </option>
                    ))}
                  </select>
                  {l.overridden && (
                    <span
                      className="ml-2 text-xs px-2 py-0.5 rounded-full bg-info-soft text-info-fg"
                      title="Set at runtime; the process restores the configured level when it restarts"
                    >
                      Runtime override
                    </span>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {problem && (
        <p role="alert" className="mt-3 text-sm text-danger-fg">
          {problem}
        </p>
      )}
      {levels && levels.length > 0 && (
        <div className="mt-4 flex items-center justify-end gap-2">
          {changed.length > 0 && (
            <span className="text-xs text-warn-fg mr-auto">{changed.length} unsaved change(s)</span>
          )}
          <button
            onClick={discard}
            disabled={busy}
            className="px-3 py-1.5 rounded-lg text-sm border border-line-strong text-fg-2 disabled:opacity-50"
          >
            Discard
          </button>
          <button
            onClick={save}
            disabled={busy || changed.length === 0}
            className="px-3 py-1.5 rounded-lg text-sm bg-accent text-on-accent hover:bg-accent-hover disabled:opacity-50"
          >
            Save log levels
          </button>
        </div>
      )}
    </div>
  );
}

function SettingsView({
  cfg,
  onSave,
}: {
  cfg: SchedulerConfigSnapshot;
  onSave: (input: Record<string, unknown>) => void;
}) {
  // Initialised once from the loaded config. Duration fields are edited as TimeSpan strings
  // ("HH:MM:SS" or "D.HH:MM:SS"), which is what the API's TimeSpan scalar accepts.
  const [form, setForm] = useState(() => ({
    manifestManagerEnabled: cfg.manifestManagerEnabled,
    jobDispatcherEnabled: cfg.jobDispatcherEnabled,
    recoverStuckJobsOnStartup: cfg.recoverStuckJobsOnStartup,
    autoPurgeDeadLetters: cfg.autoPurgeDeadLetters,
    defaultMaxRetries: cfg.defaultMaxRetries.toString(),
    retryBackoffMultiplier: cfg.retryBackoffMultiplier.toString(),
    maxActiveJobs: cfg.maxActiveJobs?.toString() ?? "",
    localWorkerCount: cfg.localWorkerCount?.toString() ?? "",
    manifestManagerPollingInterval: cfg.manifestManagerPollingInterval,
    jobDispatcherPollingInterval: cfg.jobDispatcherPollingInterval,
    defaultRetryDelay: cfg.defaultRetryDelay,
    maxRetryDelay: cfg.maxRetryDelay,
    defaultJobTimeout: cfg.defaultJobTimeout,
    stalePendingTimeout: cfg.stalePendingTimeout,
    deadLetterRetentionPeriod: cfg.deadLetterRetentionPeriod,
    metadataCleanupInterval: cfg.metadataCleanupInterval ?? "",
    metadataCleanupRetention: cfg.metadataCleanupRetention ?? "",
    failureCountWindow: cfg.failureCountWindow ?? "",
  }));

  function set<K extends keyof typeof form>(key: K, value: (typeof form)[K]) {
    setForm((f) => ({ ...f, [key]: value }));
  }

  function save() {
    const input: Record<string, unknown> = {
      manifestManagerEnabled: form.manifestManagerEnabled,
      jobDispatcherEnabled: form.jobDispatcherEnabled,
      recoverStuckJobsOnStartup: form.recoverStuckJobsOnStartup,
      autoPurgeDeadLetters: form.autoPurgeDeadLetters,
      defaultMaxRetries: Number(form.defaultMaxRetries),
      retryBackoffMultiplier: Number(form.retryBackoffMultiplier),
      manifestManagerPollingInterval: form.manifestManagerPollingInterval,
      jobDispatcherPollingInterval: form.jobDispatcherPollingInterval,
      defaultRetryDelay: form.defaultRetryDelay,
      maxRetryDelay: form.maxRetryDelay,
      defaultJobTimeout: form.defaultJobTimeout,
      stalePendingTimeout: form.stalePendingTimeout,
      deadLetterRetentionPeriod: form.deadLetterRetentionPeriod,
    };
    if (form.maxActiveJobs) input.maxActiveJobs = Number(form.maxActiveJobs);
    else input.clearMaxActiveJobs = true;
    if (form.localWorkerCount) input.localWorkerCount = Number(form.localWorkerCount);
    else input.clearLocalWorkerCount = true;
    if (form.metadataCleanupInterval)
      input.metadataCleanupInterval = form.metadataCleanupInterval;
    if (form.metadataCleanupRetention)
      input.metadataCleanupRetention = form.metadataCleanupRetention;
    if (form.failureCountWindow) input.failureCountWindow = form.failureCountWindow;
    onSave(input);
  }

  return (
    <div>
      <h1 className="text-2xl font-bold text-fg mb-6">Scheduler settings</h1>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <Card title="Services & retries">
          <Toggle
            label="Manifest manager enabled"
            checked={form.manifestManagerEnabled}
            onChange={(v) => set("manifestManagerEnabled", v)}
          />
          <Toggle
            label="Job dispatcher enabled"
            checked={form.jobDispatcherEnabled}
            onChange={(v) => set("jobDispatcherEnabled", v)}
          />
          <Toggle
            label="Recover stuck jobs on startup"
            checked={form.recoverStuckJobsOnStartup}
            onChange={(v) => set("recoverStuckJobsOnStartup", v)}
          />
          <Toggle
            label="Auto-purge dead letters"
            checked={form.autoPurgeDeadLetters}
            onChange={(v) => set("autoPurgeDeadLetters", v)}
          />
          <NumField
            label="Default max retries"
            value={form.defaultMaxRetries}
            onChange={(v) => set("defaultMaxRetries", v)}
          />
          <NumField
            label="Retry backoff multiplier"
            value={form.retryBackoffMultiplier}
            decimal
            onChange={(v) => set("retryBackoffMultiplier", v)}
          />
          <NumField
            label="Max active jobs (blank = unlimited)"
            value={form.maxActiveJobs}
            onChange={(v) => set("maxActiveJobs", v)}
          />
          <NumField
            label="Local worker count (blank = default)"
            value={form.localWorkerCount}
            onChange={(v) => set("localWorkerCount", v)}
          />
        </Card>

        <Card title="Intervals & timeouts">
          <DurationField
            label="Manifest manager poll"
            value={form.manifestManagerPollingInterval}
            onChange={(v) => set("manifestManagerPollingInterval", v)}
          />
          <DurationField
            label="Job dispatcher poll"
            value={form.jobDispatcherPollingInterval}
            onChange={(v) => set("jobDispatcherPollingInterval", v)}
          />
          <DurationField
            label="Default retry delay"
            value={form.defaultRetryDelay}
            onChange={(v) => set("defaultRetryDelay", v)}
          />
          <DurationField
            label="Max retry delay"
            value={form.maxRetryDelay}
            onChange={(v) => set("maxRetryDelay", v)}
          />
          <DurationField
            label="Default job timeout"
            value={form.defaultJobTimeout}
            onChange={(v) => set("defaultJobTimeout", v)}
          />
          <DurationField
            label="Stale pending timeout"
            value={form.stalePendingTimeout}
            onChange={(v) => set("stalePendingTimeout", v)}
          />
          <DurationField
            label="Dead letter retention"
            value={form.deadLetterRetentionPeriod}
            onChange={(v) => set("deadLetterRetentionPeriod", v)}
          />
          <DurationField
            label="Metadata cleanup interval (blank = off)"
            value={form.metadataCleanupInterval}
            onChange={(v) => set("metadataCleanupInterval", v)}
          />
          <DurationField
            label="Metadata cleanup retention (blank = off)"
            value={form.metadataCleanupRetention}
            onChange={(v) => set("metadataCleanupRetention", v)}
          />
          <DurationField
            label="Failure count window"
            hint="How far back a manifest's failed runs count toward its retry backoff and its dead letter. A manifest scheduled with its own failure window uses that instead."
            value={form.failureCountWindow}
            onChange={(v) => set("failureCountWindow", v)}
          />
        </Card>
      </div>

      <div className="mt-6">
        <button
          onClick={save}
          className="px-4 py-2 bg-accent text-on-accent rounded-lg hover:bg-accent-hover"
        >
          Save
        </button>
        <p className="text-xs text-muted mt-2">
          Durations are ISO 8601 values (PT5M = 5 minutes, PT2H = 2 hours, P1D = 1 day). Polling and
          worker-count changes take effect on the next cycle or restart.
        </p>
      </div>
    </div>
  );
}

function Card({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="bg-surface rounded-lg border border-line p-5">
      <h2 className="text-sm font-semibold text-fg mb-4">{title}</h2>
      <div className="space-y-3 text-sm">{children}</div>
    </div>
  );
}

function Toggle({
  label,
  checked,
  onChange,
}: {
  label: string;
  checked: boolean;
  onChange: (v: boolean) => void;
}) {
  return (
    <label className="flex items-center gap-2">
      <input
        type="checkbox"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
      />
      {label}
    </label>
  );
}

function NumField({
  label,
  value,
  onChange,
  decimal,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  decimal?: boolean;
}) {
  return (
    <div>
      <label className="block text-xs text-muted mb-1">{label}</label>
      <input
        value={value}
        onChange={(e) =>
          onChange(e.target.value.replace(decimal ? /[^\d.]/g : /\D/g, ""))
        }
        className="w-full border border-line-strong rounded-md px-2 py-1"
      />
    </div>
  );
}

// Number input + unit dropdown that reads/writes the raw ISO 8601 duration string the API expects.
// Local state is the source of truth for display so typing a value doesn't snap the unit.
function DurationField({
  label,
  hint,
  value,
  onChange,
}: {
  label: string;
  hint?: string;
  value: string;
  onChange: (v: string) => void;
}) {
  const [state, setState] = useState(() => parseDuration(value));
  function update(next: Partial<typeof state>) {
    const merged = { ...state, ...next };
    setState(merged);
    onChange(buildDuration(merged.value, merged.unit));
  }
  return (
    <div>
      <label className="block text-xs text-muted mb-1">{label}</label>
      {hint && <p className="text-xs text-muted mb-1">{hint}</p>}
      <div className="flex gap-2">
        <input
          type="number"
          min={0}
          aria-label={label}
          value={state.value}
          onChange={(e) => update({ value: e.target.value.replace(/\D/g, "") })}
          className="w-24 border border-line-strong rounded-md px-2 py-1"
        />
        <select
          aria-label={`${label} unit`}
          value={state.unit}
          onChange={(e) => update({ unit: e.target.value as DurationUnit })}
          className="flex-1 border border-line-strong rounded-md px-2 py-1"
        >
          {(["s", "m", "h", "d"] as DurationUnit[]).map((u) => (
            <option key={u} value={u}>
              {UNIT_LABEL[u]}
            </option>
          ))}
        </select>
      </div>
    </div>
  );
}
