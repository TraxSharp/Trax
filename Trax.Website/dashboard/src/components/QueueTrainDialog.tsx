import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useMutation } from "urql";
import { QUEUE_TRAIN } from "../graphql/mutations";
import { Spinner } from "./Spinner";
import { TrainInputForm } from "./TrainInputForm";
import { useTrainInputForm } from "../lib/useTrainInputForm";
import type { OperationResponse } from "../types";

// Modal form to queue a train for background execution (operations.workQueue.queueTrain),
// mirroring the Blazor QueueTrainDialog: on success it closes and opens the new work queue entry.
export function QueueTrainDialog({
  onClose,
  onQueued,
  initialTrain,
}: {
  onClose: () => void;
  onQueued: () => void;
  // Preselect a train (the Trains page opens the dialog for one).
  initialTrain?: string;
}) {
  const [, queueTrain] = useMutation(QUEUE_TRAIN);
  const navigate = useNavigate();
  const form = useTrainInputForm(initialTrain);
  const [priority, setPriority] = useState("0");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit() {
    if (!form.trainName) return;
    setBusy(true);
    setError(null);
    const r = await queueTrain({
      input: {
        trainName: form.trainName,
        inputJson: form.build(),
        priority: Number(priority) || 0,
      },
    });
    setBusy(false);
    if (r.error) {
      setError(r.error.message);
      return;
    }
    // A refusal (unknown train, bad input, a refusal the train makes) is success: false with a
    // message, not an error: keep the dialog open and say why.
    const result: OperationResponse | undefined = r.data?.operations?.workQueue?.queueTrain;
    if (!result?.success) {
      setError(result?.message ?? "The train was not queued.");
      return;
    }
    onQueued();
    onClose();
    if (result.id != null) navigate(`/work-queue/${result.id}`);
  }

  return (
    <div
      className="fixed inset-0 bg-black/40 flex items-center justify-center z-50"
      onClick={onClose}
    >
      <div
        className="w-full max-w-lg max-h-[90vh] overflow-y-auto bg-surface rounded-xl border border-line p-6 shadow-lg"
        onClick={(e) => e.stopPropagation()}
      >
        <h2 className="text-lg font-bold text-fg mb-4">
          Queue a train
        </h2>

        {form.selected?.hasQueueSubjectKey && (
          <p
            data-testid="queue-subject-note"
            className="text-xs rounded-lg border border-info-line bg-info-soft text-info-fg p-3 mb-4"
          >
            This train overrides <code>QueueSubjectKey</code>: its queued runs for one subject run one at a time, so
            this entry waits while another for the same subject is queued ahead of it or running.
          </p>
        )}

        <TrainInputForm form={form} />

        <label className="block text-xs text-muted mb-1">
          Priority (0-31)
        </label>
        <input
          aria-label="Priority"
          value={priority}
          onChange={(e) => setPriority(e.target.value.replace(/\D/g, ""))}
          className="w-32 text-sm border border-line-strong rounded-md px-2 py-1 mb-6 bg-field"
        />

        {error && (
          <p role="alert" className="text-sm text-danger-fg mb-4">
            {error}
          </p>
        )}

        <div className="flex justify-end gap-2">
          <button
            onClick={onClose}
            className="px-4 py-2 text-sm rounded-lg border border-line-strong text-fg-2 hover:bg-hover"
          >
            Cancel
          </button>
          <button
            onClick={submit}
            disabled={!form.trainName || busy}
            className="px-4 py-2 text-sm rounded-lg bg-accent text-on-accent hover:bg-accent-hover disabled:opacity-50 inline-flex items-center gap-1.5"
          >
            {busy && <Spinner />}
            {busy ? "Queuing…" : "Queue train"}
          </button>
        </div>
      </div>
    </div>
  );
}
