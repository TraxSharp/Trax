import type { BatchTriggerResponse } from "../types";

// How a batch trigger's answer is reported, as the Blazor dashboard's RunBatchTriggerAsync reports
// it. A refusal (no ids, too many) is an error that leaves the selection alone, so the operator can
// change it and try again. An accepted batch is the API's one-line count, a warning when it
// triggered nothing or noted an id it could not trigger as asked, and each note is shown beside it;
// the selection is cleared, because the ids that were triggered must not be sent again.
export interface BatchTriggerReport {
  refused: boolean;
  severity: "success" | "warning" | "error";
  message: string;
  notes: string[];
  clearSelection: boolean;
}

export function reportBatchTrigger(result: BatchTriggerResponse): BatchTriggerReport {
  if (!result.success)
    return {
      refused: true,
      severity: "error",
      message: result.message || "The batch was refused.",
      notes: [],
      clearSelection: false,
    };
  const triggered = result.queued + result.alreadyQueued + result.tooLateToAskAfresh;
  const notes = result.notes.map((n) => n.message);
  return {
    refused: false,
    severity: triggered === 0 || notes.length > 0 ? "warning" : "success",
    message: result.message,
    notes,
    clearSelection: true,
  };
}
