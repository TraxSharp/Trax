import { useState } from "react";
import { useMutation } from "urql";
import { UPDATE_MANIFEST } from "../graphql/mutations";
import { Modal } from "./Modal";
import { Spinner } from "./Spinner";
import { toast } from "../lib/toast";
import type { ManifestSummary, ScheduleType } from "../types";

const SCHEDULE_TYPES: ScheduleType[] = [
  "NONE",
  "CRON",
  "INTERVAL",
  "ON_DEMAND",
  "DEPENDENT",
];

// Patch a manifest's mutable scheduling/retry settings. Fields not shown here (name,
// group, dependency) are structural and set at registration, not editable at runtime.
export function ManifestEditForm({
  manifest,
  onSaved,
  onClose,
}: {
  manifest: ManifestSummary;
  onSaved: () => void;
  onClose: () => void;
}) {
  const [scheduleType, setScheduleType] = useState<ScheduleType>(manifest.scheduleType);
  const [cron, setCron] = useState(manifest.cronExpression ?? "");
  const [interval, setInterval] = useState(
    manifest.intervalSeconds != null ? String(manifest.intervalSeconds) : "",
  );
  const [maxRetries, setMaxRetries] = useState(String(manifest.maxRetries));
  const [priority, setPriority] = useState(String(manifest.priority));
  const [timeout, setTimeout] = useState(
    manifest.timeoutSeconds != null ? String(manifest.timeoutSeconds) : "",
  );
  const [{ fetching }, update] = useMutation(UPDATE_MANIFEST);

  async function onSubmit() {
    const timeoutTrimmed = timeout.trim();
    const input: Record<string, unknown> = {
      scheduleType,
      cronExpression: scheduleType === "CRON" ? cron.trim() || null : null,
      intervalSeconds:
        scheduleType === "INTERVAL" && interval.trim() ? Number(interval) : null,
      maxRetries: Number(maxRetries),
      priority: Number(priority),
    };
    if (timeoutTrimmed === "") input.clearTimeout = true;
    else input.timeoutSeconds = Number(timeoutTrimmed);

    const r = await update({ id: manifest.id, input });
    if (r.error) {
      toast(r.error.message, "error");
      return;
    }
    if (r.data?.operations.updateManifest.success) {
      toast("Manifest updated.", "success");
      onSaved();
      onClose();
    } else {
      toast(r.data?.operations.updateManifest.message ?? "Update failed.", "error");
    }
  }

  return (
    <Modal title="Edit manifest" onClose={onClose}>
      <div className="space-y-3">
        <Field label="Schedule type">
          <select
            value={scheduleType}
            onChange={(e) => setScheduleType(e.target.value as ScheduleType)}
            className={inputClass}
          >
            {SCHEDULE_TYPES.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </select>
        </Field>

        {scheduleType === "CRON" && (
          <Field label="Cron expression">
            <input
              value={cron}
              onChange={(e) => setCron(e.target.value)}
              placeholder="0 */5 * * * *"
              className={inputClass}
            />
          </Field>
        )}

        {scheduleType === "INTERVAL" && (
          <Field label="Interval (seconds)">
            <input
              type="number"
              min={1}
              value={interval}
              onChange={(e) => setInterval(e.target.value)}
              className={inputClass}
            />
          </Field>
        )}

        <div className="grid grid-cols-2 gap-3">
          <Field label="Max retries">
            <input
              type="number"
              min={0}
              value={maxRetries}
              onChange={(e) => setMaxRetries(e.target.value)}
              className={inputClass}
            />
          </Field>
          <Field label="Priority">
            <input
              type="number"
              value={priority}
              onChange={(e) => setPriority(e.target.value)}
              className={inputClass}
            />
          </Field>
        </div>

        <Field label="Timeout (seconds, blank = none)">
          <input
            type="number"
            min={1}
            value={timeout}
            onChange={(e) => setTimeout(e.target.value)}
            className={inputClass}
          />
        </Field>
      </div>

      <div className="mt-5 flex justify-end gap-2">
        <button
          onClick={onClose}
          className="px-3 py-2 rounded-lg text-sm text-fg-2 hover:bg-hover"
        >
          Cancel
        </button>
        <button
          onClick={onSubmit}
          disabled={fetching}
          className="px-3 py-2 rounded-lg text-sm font-medium bg-accent text-on-accent hover:bg-accent-hover disabled:opacity-50 inline-flex items-center gap-1.5"
        >
          {fetching && <Spinner />}
          {fetching ? "Saving…" : "Save changes"}
        </button>
      </div>
    </Modal>
  );
}

const inputClass =
  "w-full rounded-lg border border-line-strong bg-field px-3 py-2 text-sm text-fg focus:outline-none focus:ring-2 focus:ring-accent";

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="block">
      <span className="block text-sm text-fg-2 mb-1">{label}</span>
      {children}
    </label>
  );
}
