import { useId, useState } from "react";
import { confirmAction } from "../lib/confirm";
import { toast } from "../lib/toast";
import { formatTime } from "../lib/format";
import { usePoll } from "../lib/poll";
import { useAnswers } from "../lib/answerable";
import { useRefetchOnChange } from "../lib/useRefetchOnChange";
import { useTraxMutation, useTraxQuery } from "../lib/consoleClient";
import { DEFAULT_DRAIN_MINUTES, drainSetBy, parseDrainMinutes } from "../lib/drain";
import { drainDocument, drainHostsDocument, resumeHostsDocument } from "../trax/index.g";

const DOCS = "https://traxsharp.net/docs/dashboard/system#drain-and-resume";

// Drain and resume (operations.config.drain and resume): the same service a deploy's drain writes
// through, so a drain set here and one a deploy set read the same. The header's Drained mark reads
// the same query. The demo's recordings hold no drain read, so the demo does not offer it
// (lib/answerable.ts).
export function DrainPanel() {
  const answered = useAnswers("Drain");
  const [{ data, error }, reexecute] = useTraxQuery(drainDocument, { pause: !answered });
  const [, drainHosts] = useTraxMutation(drainHostsDocument);
  const [, resumeHosts] = useTraxMutation(resumeHostsDocument);
  const [minutes, setMinutes] = useState(String(DEFAULT_DRAIN_MINUTES));
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const minutesId = useId();
  usePoll(() => {
    if (answered) reexecute();
  });
  useRefetchOnChange("SCHEDULER_CONFIG", () => {
    if (answered) reexecute();
  });
  if (!answered) return null;

  const drain = data?.operations?.config?.drain;

  async function act(kind: "drain" | "resume") {
    setProblem(null);
    let length: number | undefined;
    if (kind === "drain") {
      const parsed = parseDrainMinutes(minutes);
      if (typeof parsed === "string") {
        setProblem(parsed);
        return;
      }
      length = parsed;
    }
    const ok = await confirmAction(
      kind === "drain"
        ? {
            title: "Drain every host",
            message:
              `For ${length} minutes no host starts work from the queue, queues a scheduled run or picks up a job. ` +
              "Work already running finishes. A schedule that comes due meanwhile is handled by its manifest's " +
              "misfire policy once the drain ends.",
            confirmLabel: "Drain",
            danger: true,
          }
        : {
            title: "Resume every host",
            message: "Every host starts taking work again within a few seconds, whoever set the drain.",
            confirmLabel: "Resume",
          },
    );
    if (!ok) return;
    setBusy(true);
    let res: { success: boolean; message: string | null } | undefined;
    let failed: string | undefined;
    if (kind === "drain") {
      const r = await drainHosts({ minutes: length });
      failed = r.error?.message;
      res = r.data?.operations?.config.drain;
    } else {
      const r = await resumeHosts();
      failed = r.error?.message;
      res = r.data?.operations?.config.resume;
    }
    setBusy(false);
    if (failed) setProblem(failed);
    else if (!res?.success) setProblem(res?.message || "Nothing changed.");
    else toast(res.message || (kind === "drain" ? "Drained." : "Resumed."), "success");
    reexecute();
  }

  return (
    <section id="drain" aria-labelledby="scheduler-drain" className="bg-surface rounded-lg border border-line p-5 mb-6">
      <h2 id="scheduler-drain" className="text-sm font-semibold text-fg mb-1">
        Drain and resume
      </h2>
      <p className="text-xs text-muted mb-4">
        A drain stops every host starting new work while what is running finishes, so the hosts can be replaced
        without cutting a run short. A deploy drains before it replaces the hosts and resumes after.{" "}
        <a href={DOCS} target="_blank" rel="noreferrer" className="text-accent-fg hover:underline">
          How a drain works
        </a>
      </p>

      {error && (
        <p role="alert" className="text-sm text-danger-fg mb-3">
          {error.message}
        </p>
      )}
      {!drain && !error && <p className="text-muted text-sm">Loading…</p>}
      {drain && (
        <>
          <p className="text-sm text-fg mb-3" data-testid="drain-state">
            <span
              className={`mr-2 text-xs px-2 py-0.5 rounded-full ${
                drain.drained ? "bg-warn-soft text-warn-fg" : "bg-ok-soft text-ok-fg"
              }`}
            >
              {drain.drained ? "Drained" : "Taking work"}
            </span>
            {drain.drained
              ? `Drained until ${formatTime(drain.until)}, set by ${drainSetBy(drain.startedBy)} at ${formatTime(drain.startedAt)}.`
              : "Not drained: every host starts work as usual."}
          </p>

          <div className="flex flex-wrap items-end gap-3 mb-4">
            {!drain.drained && (
              <label htmlFor={minutesId} className="text-xs text-muted flex flex-col gap-1">
                Drain for (minutes)
                <input
                  id={minutesId}
                  inputMode="numeric"
                  value={minutes}
                  onChange={(e) => setMinutes(e.target.value)}
                  className="w-24 px-2 py-1 rounded-lg border border-line bg-raised text-fg text-sm"
                />
              </label>
            )}
            {drain.drained ? (
              <button
                onClick={() => act("resume")}
                disabled={busy}
                className="px-3 py-1.5 rounded-lg text-sm disabled:opacity-50 bg-accent text-on-accent hover:bg-accent-hover"
              >
                Resume
              </button>
            ) : (
              <button
                onClick={() => act("drain")}
                disabled={busy}
                className="px-3 py-1.5 rounded-lg text-sm disabled:opacity-50 border border-warn-line text-warn-fg hover:bg-warn-soft"
              >
                Drain
              </button>
            )}
          </div>

          <h3 className="text-xs font-semibold text-fg mb-2">
            {drain.busyTotal === 0
              ? "Nothing is busy"
              : `Busy: ${drain.busyTotal} ${drain.busyTotal === 1 ? "item" : "items"}${
                  drain.busyTotal > drain.busy.length ? `, the first ${drain.busy.length} shown` : ""
                }`}
          </h3>
          {drain.busy.length > 0 && (
            <table aria-label="Busy work" className="w-full text-xs">
              <thead className="text-muted text-left">
                <tr>
                  <th className="py-1 pr-3 font-medium">Kind</th>
                  <th className="py-1 pr-3 font-medium">Id</th>
                  <th className="py-1 pr-3 font-medium">Train or machine</th>
                  <th className="py-1 pr-3 font-medium">State</th>
                  <th className="py-1 pr-3 font-medium">Since</th>
                  <th className="py-1 font-medium">Host</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-line">
                {drain.busy.map((b) => (
                  <tr key={`${b.kind}:${b.id}`}>
                    <td className="py-1 pr-3">{b.kind === "run" ? "Run" : "State machine"}</td>
                    <td className="py-1 pr-3 font-mono">{b.id}</td>
                    <td className="py-1 pr-3">{b.name}</td>
                    <td className="py-1 pr-3">{b.state}</td>
                    <td className="py-1 pr-3">{formatTime(b.since)}</td>
                    <td className="py-1">{b.host ?? "—"}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </>
      )}
      {problem && (
        <p role="alert" className="mt-3 text-sm text-danger-fg">
          {problem}
        </p>
      )}
    </section>
  );
}
