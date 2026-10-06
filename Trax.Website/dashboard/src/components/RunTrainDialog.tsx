import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useMutation } from "urql";
import { RUN_TRAIN } from "../graphql/mutations";
import { Spinner } from "./Spinner";
import { TrainInputForm } from "./TrainInputForm";
import { useTrainInputForm } from "../lib/useTrainInputForm";
import type { OperationResponse } from "../types";

// Modal form to run a train now (operations.workQueue.runTrain): the input goes to the host's job
// submitter with no work queue entry, so dispatch never sees it. Mirrors the Blazor RunTrainDialog:
// on success it closes and opens the new run.
export function RunTrainDialog({
  onClose,
  initialTrain,
}: {
  onClose: () => void;
  initialTrain?: string;
}) {
  const [, runTrain] = useMutation(RUN_TRAIN);
  const navigate = useNavigate();
  const form = useTrainInputForm(initialTrain);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function submit() {
    if (!form.trainName) return;
    setBusy(true);
    setError(null);
    const r = await runTrain({ input: { trainName: form.trainName, inputJson: form.build() } });
    setBusy(false);
    if (r.error) {
      setError(r.error.message);
      return;
    }
    // A refusal (unknown train, bad input, no job submitter) keeps the dialog open with its reason.
    const result: OperationResponse | undefined = r.data?.operations?.workQueue?.runTrain;
    if (!result?.success) {
      setError(result?.message ?? "The train did not run.");
      return;
    }
    onClose();
    if (result.id != null) navigate(`/executions/${result.id}`);
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
        <h2 className="text-lg font-bold text-fg mb-1">Run a train</h2>
        <p className="text-xs text-muted mb-4">
          Runs now through the host&apos;s job submitter, without a work queue entry.
        </p>

        {/* Run submits the input to the job submitter now: no work queue entry, so dispatch never
            sees it and QueueSubjectKey is not consulted. Only a subject-keyed train has anything to
            bypass, so only its dialog warns, in the Blazor dialog's words. */}
        {form.selected?.hasQueueSubjectKey && (
          <div
            role="alert"
            data-testid="run-subject-bypass-warning"
            className="text-sm rounded-lg border border-warn-line bg-warn-soft text-warn-fg p-3 mb-4"
          >
            Run bypasses subject serialization. This train overrides <code>QueueSubjectKey</code>, so this run may
            execute concurrently with queued or in-flight work for the same subject. Use <strong>Queue</strong>{" "}
            instead to wait for the subject to be free.
          </div>
        )}

        <TrainInputForm form={form} />

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
            {busy ? "Running…" : "Run train"}
          </button>
        </div>
      </div>
    </div>
  );
}
