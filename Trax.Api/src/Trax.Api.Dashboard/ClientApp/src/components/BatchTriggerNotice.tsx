import type { BatchTriggerReport } from "../lib/batchTrigger";

/**
 * What a batch trigger left to say after its toast: the refusal, or the ids it could not trigger
 * as asked (not found, or claimed too late to ask afresh), as the Blazor pages show them under
 * their batch buttons. Nothing for a batch that triggered every id.
 */
export function BatchTriggerNotice({ report, error }: { report: BatchTriggerReport | null; error: string | null }) {
  const text = error ?? (report?.refused ? report.message : null);
  if (text)
    return (
      <div
        role="alert"
        className="bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-3 text-sm mb-3"
      >
        {text}
      </div>
    );
  if (!report || report.notes.length === 0) return null;
  return (
    <div
      role="alert"
      className="bg-warn-soft border border-warn-line text-warn-fg rounded-lg p-3 text-sm mb-3"
    >
      <ul className="list-disc ml-4">
        {report.notes.map((n, i) => (
          <li key={i}>{n}</li>
        ))}
      </ul>
    </div>
  );
}
