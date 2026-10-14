import type { TrainState } from "../types";

// Three-step lifecycle: Pending → In progress → terminal (Completed / Failed / Cancelled),
// mirroring the Blazor dashboard's StateTimeline.
export function StateTimeline({
  state,
  startTime,
  endTime,
}: {
  state: TrainState;
  startTime: string;
  endTime: string | null;
}) {
  const inProgressReached = state !== "PENDING";
  const terminalReached =
    state === "COMPLETED" || state === "FAILED" || state === "CANCELLED";

  const terminalTone =
    state === "COMPLETED"
      ? "bg-ok"
      : state === "FAILED"
        ? "bg-danger"
        : "bg-warn";

  const steps = [
    { label: "Pending", reached: true, tone: "bg-idle" },
    {
      label: "In progress",
      reached: inProgressReached,
      tone: state === "IN_PROGRESS" ? "bg-info" : "bg-idle",
    },
    {
      label:
        state === "FAILED"
          ? "Failed"
          : state === "CANCELLED"
            ? "Cancelled"
            : "Completed",
      reached: terminalReached,
      tone: terminalReached ? terminalTone : "bg-line-strong",
    },
  ];

  const runtime =
    endTime != null
      ? `${((new Date(endTime).getTime() - new Date(startTime).getTime()) / 1000).toFixed(1)}s`
      : null;

  return (
    <div className="bg-surface rounded-lg border border-line p-5 mb-6">
      <h2 className="text-sm font-semibold text-fg mb-4">
        State timeline
      </h2>
      <div className="flex items-center">
        {steps.map((step, i) => (
          <div key={step.label} className="flex items-center flex-1 last:flex-none">
            <div className="flex flex-col items-center">
              <div
                className={`h-4 w-4 rounded-full ${
                  step.reached ? step.tone : "bg-raised"
                }`}
              />
              <span
                className={`mt-1.5 text-xs ${
                  step.reached
                    ? "text-fg-2"
                    : "text-muted"
                }`}
              >
                {step.label}
              </span>
            </div>
            {i < steps.length - 1 && (
              <div
                className={`h-0.5 flex-1 mx-2 mb-5 ${
                  steps[i + 1].reached
                    ? "bg-idle"
                    : "bg-raised"
                }`}
              />
            )}
          </div>
        ))}
      </div>
      {runtime && (
        <p className="text-xs text-muted mt-3">
          Ran for {runtime}
        </p>
      )}
    </div>
  );
}
