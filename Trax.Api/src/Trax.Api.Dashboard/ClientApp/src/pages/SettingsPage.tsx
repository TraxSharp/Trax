import { useId, useState } from "react";
import { useMutation, useQuery } from "urql";
import { LOG_LEVELS, SCHEDULER_CONFIG } from "../graphql/queries";
import { SET_LOG_LEVELS, UPDATE_SCHEDULER } from "../graphql/mutations";
import { toast } from "../lib/toast";
import { confirmAction } from "../lib/confirm";
import { changedLogLevels, LOG_LEVEL_NAMES, notAppliedMessage } from "../lib/logLevels";
import { buildDuration, parseDuration, UNIT_LABEL, type DurationUnit } from "../lib/duration";
import type { LogLevelSetting, SchedulerConfigSnapshot, SetLogLevelsResponse } from "../types";

interface ConfigData {
  operations: { config: { scheduler: SchedulerConfigSnapshot } };
}

type Patch = Record<string, unknown>;
type Save = (patch: Patch) => Promise<string | null>;

const DOCS = "https://traxsharp.net/docs/scheduler";

// The scheduler's runtime settings, one section per concern, each saved on its own: a save sends
// only the fields that section changed (updateScheduler is a patch), so saving the retry policy
// never rewrites a switch another operator just flipped. The two switches are Pause and Resume
// actions, confirmed, and the header's paused badge reads the same query this page refetches.
export function SettingsPage() {
  const [result, reexecute] = useQuery<ConfigData>({ query: SCHEDULER_CONFIG });
  const [, update] = useMutation(UPDATE_SCHEDULER);
  const cfg = result.data?.operations?.config?.scheduler;

  /** Sends one patch; null when it was saved, else what the server said. */
  const save: Save = async (input) => {
    const r = await update({ input });
    if (r.error) return r.error.message;
    const res = r.data?.operations?.config?.updateScheduler;
    if (!res?.success) return res?.message || "Not saved.";
    toast(res.message || "Saved.", "success");
    reexecute({ requestPolicy: "network-only" });
    return null;
  };

  return (
    <div>
      <h1 className="text-2xl font-bold text-fg mb-2">Scheduler</h1>
      <p className="text-sm text-muted mb-6">
        These settings are stored in the database and shared by every scheduler host. A saved change applies at once
        on the host serving this page and on every other scheduler host within a few seconds, unless its section says
        otherwise.{" "}
        <a href={`${DOCS}/setup`} target="_blank" rel="noreferrer" className="text-accent-fg hover:underline">
          How the scheduler is set up
        </a>
      </p>

      {result.error && (
        <div className="bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-4 text-sm mb-6">
          {result.error.message}
        </div>
      )}
      {!cfg && !result.error && <p className="text-muted">Loading…</p>}
      {cfg && <SchedulerSections cfg={cfg} save={save} />}

      <LogLevelsPanel />
    </div>
  );
}

function SchedulerSections({ cfg, save }: { cfg: SchedulerConfigSnapshot; save: Save }) {
  // A section is remounted when the values it shows change on the server, so a save (here or by
  // another operator) shows what is stored; an edit in another section is kept.
  const keyOf = (fields: Field[]) => JSON.stringify(fields.map((f) => cfg[f.key]));
  return (
    <div className="space-y-6">
      <Switches cfg={cfg} save={save} />
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {SECTIONS.map((s) => {
          const fields = s.fields.filter((f) => !f.onlyWhenSet || cfg[f.key] != null);
          return <SettingsSection key={`${s.title}:${keyOf(fields)}`} section={s} fields={fields} cfg={cfg} save={save} />;
        })}
      </div>
    </div>
  );
}

// ── Pause and resume ──────────────────────────────────────────────────────

interface Loop {
  key: "manifestManagerEnabled" | "jobDispatcherEnabled";
  name: string;
  does: string;
  paused: string;
}

const LOOPS: Loop[] = [
  {
    key: "manifestManagerEnabled",
    name: "Manifest manager",
    does: "Queues each manifest's run when its schedule comes due.",
    paused: "No manifest is queued on its schedule. Work queued by hand is still started.",
  },
  {
    key: "jobDispatcherEnabled",
    name: "Job dispatcher",
    does: "Starts queued work on a worker.",
    paused: "Queued work waits and nothing new starts from the queue. Runs already started finish.",
  },
];

function Switches({ cfg, save }: { cfg: SchedulerConfigSnapshot; save: Save }) {
  const [busy, setBusy] = useState<string | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  async function flip(loop: Loop) {
    const running = cfg[loop.key];
    const verb = running ? "Pause" : "Resume";
    const title = `${verb} ${loop.name.toLowerCase()}`;
    const ok = await confirmAction({
      title,
      message: running
        ? `${loop.paused} This applies to every scheduler host within a few seconds, until someone resumes it.`
        : `The ${loop.name.toLowerCase()} starts again on every scheduler host within a few seconds.`,
      danger: running,
    });
    if (!ok) return;
    setProblem(null);
    setBusy(loop.key);
    const refused = await save({ [loop.key]: !running });
    setBusy(null);
    if (refused) setProblem(`${title}: ${refused}`);
  }

  return (
    <section aria-labelledby="scheduler-switches" className="bg-surface rounded-lg border border-line p-5">
      <h2 id="scheduler-switches" className="text-sm font-semibold text-fg mb-1">
        Pause and resume
      </h2>
      <p className="text-xs text-muted mb-4">
        Takes effect at once on this host and within a few seconds on every scheduler host. While either is paused,
        the header says so on every page.
      </p>
      <ul className="divide-y divide-line">
        {LOOPS.map((loop) => {
          const running = cfg[loop.key];
          return (
            <li key={loop.key} aria-label={loop.name} className="py-3 flex flex-wrap items-center gap-3">
              <div className="flex-1 min-w-[16rem]">
                <p className="text-sm font-medium text-fg">
                  {loop.name}{" "}
                  <span
                    className={`ml-1 text-xs px-2 py-0.5 rounded-full ${
                      running ? "bg-ok-soft text-ok-fg" : "bg-warn-soft text-warn-fg"
                    }`}
                  >
                    {running ? "Running" : "Paused"}
                  </span>
                </p>
                <p className="text-xs text-muted">{running ? loop.does : loop.paused}</p>
              </div>
              <button
                onClick={() => flip(loop)}
                disabled={busy !== null}
                className={`px-3 py-1.5 rounded-lg text-sm disabled:opacity-50 ${
                  running
                    ? "border border-warn-line text-warn-fg hover:bg-warn-soft"
                    : "bg-accent text-on-accent hover:bg-accent-hover"
                }`}
              >
                {running ? "Pause" : "Resume"} {loop.name.toLowerCase()}
              </button>
            </li>
          );
        })}
      </ul>
      {problem && (
        <p role="alert" className="mt-3 text-sm text-danger-fg">
          {problem}
        </p>
      )}
    </section>
  );
}

// ── The other settings, a section each ─────────────────────────────────────

type Key = keyof SchedulerConfigSnapshot;

interface Field {
  key: Key;
  label: string;
  kind: "duration" | "int" | "decimal" | "bool";
  hint?: string;
  /** Blank is allowed and sends this flag instead (no limit, or the host's default). */
  clear?: string;
  /** Shown only when the host reports a value (it has local workers, or metadata cleanup). */
  onlyWhenSet?: boolean;
}

interface Section {
  title: string;
  /** When a saved change takes effect. */
  effect: string;
  docs: string;
  fields: Field[];
}

const SECTIONS: Section[] = [
  {
    title: "Polling and queue",
    effect:
      "Intervals apply from each loop's next wait and the limit from the dispatcher's next cycle. The local worker count is saved at once but used only when a host next starts, because a host starts its workers then.",
    docs: `${DOCS}/concurrency`,
    fields: [
      { key: "manifestManagerPollingInterval", label: "Manifest manager checks schedules every", kind: "duration" },
      { key: "jobDispatcherPollingInterval", label: "Job dispatcher checks the queue every", kind: "duration" },
      {
        key: "maxActiveJobs",
        label: "Max active jobs, all groups (blank = no limit)",
        kind: "int",
        clear: "clearMaxActiveJobs",
        hint: "The most runs the dispatcher keeps active at once across every manifest group. A manifest group's own limit is set on the group's page, under Schedules.",
      },
      {
        key: "localWorkerCount",
        label: "Local workers per host (blank = one per processor)",
        kind: "int",
        clear: "clearLocalWorkerCount",
        onlyWhenSet: true,
      },
    ],
  },
  {
    title: "Retry",
    effect: "Applies to the next retry the scheduler schedules, within a few seconds on every scheduler host.",
    docs: `${DOCS}/how-retries-work`,
    fields: [
      {
        key: "defaultMaxRetries",
        label: "Default max retries",
        kind: "int",
        hint: "Retries after a failed first run before the manifest becomes a dead letter. A manifest that sets its own uses that.",
      },
      { key: "defaultRetryDelay", label: "Default retry delay", kind: "duration" },
      { key: "retryBackoffMultiplier", label: "Retry backoff multiplier", kind: "decimal", hint: "1 keeps the delay constant." },
      { key: "maxRetryDelay", label: "Max retry delay", kind: "duration" },
      {
        key: "failureCountWindow",
        label: "Failure count window",
        kind: "duration",
        onlyWhenSet: true,
        hint: "How far back a manifest's failed runs count toward its retry backoff and its dead letter. A manifest scheduled with its own failure window uses that instead.",
      },
    ],
  },
  {
    title: "Job timeouts",
    effect:
      "Timeouts apply from the scheduler's next cycle, within a few seconds on every scheduler host. Recovery on start applies the next time a host starts.",
    docs: `${DOCS}/cancellation-and-timeouts`,
    fields: [
      {
        key: "defaultJobTimeout",
        label: "Default job timeout",
        kind: "duration",
        hint: "A dispatched run whose manifest sets no timeout is cancelled after this long.",
      },
      {
        key: "stalePendingTimeout",
        label: "Stale pending timeout",
        kind: "duration",
        hint: "A run still pending this long after it was dispatched is marked failed.",
      },
      {
        key: "recoverStuckJobsOnStartup",
        label: "Fail runs left in progress when a host starts",
        kind: "bool",
        hint: "Every in-progress run that started before the host did is marked failed, whichever host was running it.",
      },
    ],
  },
  {
    title: "Retention and purge",
    effect: "Applies from each cleanup's next pass, on every scheduler host.",
    docs: `${DOCS}/dead-letters-and-cleanup`,
    fields: [
      { key: "autoPurgeDeadLetters", label: "Delete resolved dead letters automatically", kind: "bool" },
      { key: "deadLetterRetentionPeriod", label: "Keep resolved dead letters for", kind: "duration" },
      { key: "metadataCleanupInterval", label: "Run metadata cleanup every", kind: "duration", onlyWhenSet: true },
      { key: "metadataCleanupRetention", label: "Keep scheduler train runs for", kind: "duration", onlyWhenSet: true },
    ],
  },
];

/** The value a field edits, as the form holds it. */
function initial(f: Field, cfg: SchedulerConfigSnapshot): string | boolean {
  const v = cfg[f.key];
  if (f.kind === "bool") return Boolean(v);
  return v == null ? "" : String(v);
}

function SettingsSection({
  section,
  fields,
  cfg,
  save,
}: {
  section: Section;
  fields: Field[];
  cfg: SchedulerConfigSnapshot;
  save: Save;
}) {
  const [start] = useState(() => Object.fromEntries(fields.map((f) => [f.key, initial(f, cfg)])));
  const [form, setForm] = useState(start);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const changed = fields.filter((f) => form[f.key] !== start[f.key]);
  const id = `section-${section.title.replace(/\W+/g, "-").toLowerCase()}`;

  async function submit() {
    setProblem(null);
    const patch: Patch = {};
    for (const f of changed) {
      const v = form[f.key];
      if (f.kind === "bool") patch[f.key] = v;
      else if (v === "" && f.clear) patch[f.clear] = true;
      else if (v === "") {
        setProblem(`${f.label} needs a value.`);
        return;
      } else patch[f.key] = f.kind === "duration" ? v : Number(v);
    }
    setBusy(true);
    const refused = await save(patch);
    setBusy(false);
    if (refused) setProblem(refused);
  }

  return (
    <section
      aria-labelledby={id}
      className={`bg-surface rounded-lg border p-5 ${changed.length > 0 ? "border-warn-line" : "border-line"}`}
    >
      <h2 id={id} className="text-sm font-semibold text-fg mb-1">
        {section.title}
      </h2>
      <p className="text-xs text-muted mb-4">
        {section.effect}{" "}
        <a href={section.docs} target="_blank" rel="noreferrer" className="text-accent-fg hover:underline">
          Docs
        </a>
      </p>
      <div className="space-y-3 text-sm">
        {fields.map((f) => {
          const v = form[f.key];
          const set = (next: string | boolean) => setForm((cur) => ({ ...cur, [f.key]: next }));
          if (f.kind === "bool")
            return <Toggle key={f.key} label={f.label} hint={f.hint} checked={v as boolean} onChange={set} />;
          if (f.kind === "duration")
            return <DurationField key={f.key} label={f.label} hint={f.hint} value={v as string} onChange={set} />;
          return (
            <NumField key={f.key} label={f.label} hint={f.hint} value={v as string} decimal={f.kind === "decimal"} onChange={set} />
          );
        })}
      </div>
      {problem && (
        <p role="alert" className="mt-3 text-sm text-danger-fg">
          {problem}
        </p>
      )}
      <div className="mt-4 flex items-center justify-end gap-2">
        {changed.length > 0 && <span className="text-xs text-warn-fg mr-auto">{changed.length} unsaved change(s)</span>}
        <button
          onClick={() => {
            setForm(start);
            setProblem(null);
          }}
          disabled={busy || changed.length === 0}
          className="px-3 py-1.5 rounded-lg text-sm border border-line-strong text-fg-2 disabled:opacity-50"
        >
          Discard
        </button>
        <button
          onClick={submit}
          disabled={busy || changed.length === 0}
          className="px-3 py-1.5 rounded-lg text-sm bg-accent text-on-accent hover:bg-accent-hover disabled:opacity-50"
        >
          Save {section.title.toLowerCase()}
        </button>
      </div>
    </section>
  );
}

// ── Log levels ────────────────────────────────────────────────────────────

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
    <section
      aria-labelledby="log-levels"
      className={`mt-6 bg-surface rounded-lg border p-5 ${changed.length > 0 ? "border-warn-line" : "border-line"}`}
    >
      <h2 id="log-levels" className="text-sm font-semibold text-fg mb-1">
        Log levels
      </h2>
      <p className="text-xs text-muted mb-4">
        Not stored with the settings above: a change takes effect at once, only in the process serving this page, and
        lasts until it restarts. Scheduler and worker processes keep their own levels.
      </p>
      {error && <p className="text-sm text-danger-fg">{error.message}</p>}
      {levels && levels.length === 0 && <p className="text-sm text-muted">No log levels are configured.</p>}
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
          {changed.length > 0 && <span className="text-xs text-warn-fg mr-auto">{changed.length} unsaved change(s)</span>}
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
    </section>
  );
}

// ── Fields ────────────────────────────────────────────────────────────────

function Hint({ text }: { text?: string }) {
  return text ? <p className="text-xs text-muted mb-1">{text}</p> : null;
}

function Toggle({
  label,
  hint,
  checked,
  onChange,
}: {
  label: string;
  hint?: string;
  checked: boolean;
  onChange: (v: boolean) => void;
}) {
  return (
    <div>
      <label className="flex items-center gap-2">
        <input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} />
        {label}
      </label>
      <Hint text={hint} />
    </div>
  );
}

function NumField({
  label,
  hint,
  value,
  onChange,
  decimal,
}: {
  label: string;
  hint?: string;
  value: string;
  onChange: (v: string) => void;
  decimal?: boolean;
}) {
  const id = useId();
  return (
    <div>
      <label htmlFor={id} className="block text-xs text-muted mb-1">
        {label}
      </label>
      <Hint text={hint} />
      <input
        id={id}
        value={value}
        inputMode={decimal ? "decimal" : "numeric"}
        onChange={(e) => onChange(e.target.value.replace(decimal ? /[^\d.]/g : /\D/g, ""))}
        className="w-full border border-line-strong rounded-md px-2 py-1 bg-field"
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
  // Discard puts the form back; follow it when the value no longer matches what is typed.
  const shown = buildDuration(state.value, state.unit);
  const current = shown === value || (value === "" && state.value === "") ? state : parseDuration(value);
  function update(next: Partial<typeof state>) {
    const merged = { ...current, ...next };
    setState(merged);
    onChange(buildDuration(merged.value, merged.unit));
  }
  return (
    <div>
      <p className="block text-xs text-muted mb-1">{label}</p>
      <Hint text={hint} />
      <div className="flex gap-2">
        <input
          type="number"
          min={0}
          aria-label={label}
          value={current.value}
          onChange={(e) => update({ value: e.target.value.replace(/\D/g, "") })}
          className="w-24 border border-line-strong rounded-md px-2 py-1 bg-field"
        />
        <select
          aria-label={`${label} unit`}
          value={current.unit}
          onChange={(e) => update({ unit: e.target.value as DurationUnit })}
          className="flex-1 border border-line-strong rounded-md px-2 py-1 bg-field"
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
