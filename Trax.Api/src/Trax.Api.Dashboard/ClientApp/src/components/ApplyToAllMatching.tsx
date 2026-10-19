import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { useMutation } from "urql";
import { START_BULK_OPERATION } from "../graphql/mutations";
import { useAnswers } from "../lib/answerable";
import { confirmAction } from "../lib/confirm";
import { toast } from "../lib/toast";
import {
  BULK_KINDS,
  type BulkOperationFilter,
  type BulkOperationKind,
  type StartBulkOperationResponse,
} from "../lib/bulkOperations";
import { NotePrompt } from "./NotePrompt";

export interface BulkAction {
  kind: BulkOperationKind;
  /** What it does to each row, after "every run this filter matches: N." */
  explain: string;
  danger?: boolean;
}

/**
 * The batch bar's "all N matching" buttons: each starts a bulk operation (api/0045) over the list's
 * filter, the one its read sends, behind a confirmation that asks for the matched count to be typed.
 * The server matches the filter again when it starts and refuses unless the count is the same; the
 * new count is then shown and confirmed again. A started operation opens on its own page.
 */
export function ApplyToAllMatching({
  total,
  filter,
  actions,
  unavailable,
}: {
  /** How many rows the list says match; nothing is offered while it is unknown or zero. */
  total: number | undefined;
  filter: BulkOperationFilter;
  actions: BulkAction[];
  /** Why the filter as set cannot take these actions (it matches rows that have ended); shown instead. */
  unavailable?: string;
}) {
  const navigate = useNavigate();
  const [, start] = useMutation(START_BULK_OPERATION);
  const [busy, setBusy] = useState(false);
  const [askNote, setAskNote] = useState<{ title: string; resolve: (note: string | null) => void } | null>(null);
  // The demo offers it only once its recordings hold the operations it opens (lib/answerable.ts).
  const offered = useAnswers("BulkOperations");
  if (!offered || !total) return null;

  const note = (title: string) => new Promise<string | null>((resolve) => setAskNote({ title, resolve }));
  const closeNote = (value: string | null) => {
    askNote?.resolve(value);
    setAskNote(null);
  };

  async function apply(action: BulkAction) {
    const k = BULK_KINDS[action.kind];
    const title = `${k.verb} all matching ${k.noun}`;
    let expected = total!;
    let changed = "";
    let acknowledgement: string | null = null;
    if (action.kind === "ACKNOWLEDGE") {
      acknowledgement = await note(title);
      if (acknowledgement == null) return;
    }
    for (;;) {
      const ok = await confirmAction({
        title,
        message: `${changed}${k.verb} every one of the ${k.noun} this filter matches: ${expected.toLocaleString()}. ${action.explain} It runs in the background; its page follows it.`,
        danger: action.danger ?? true,
        typeToConfirm: String(expected),
      });
      if (!ok) return;
      setBusy(true);
      const r = await start({
        kind: action.kind,
        filter,
        expectedCount: expected,
        askAfresh: false,
        note: acknowledgement,
      });
      setBusy(false);
      const res: StartBulkOperationResponse | undefined = r.data?.operations?.startBulkOperation;
      if (r.error || !res) {
        toast(r.error?.message ?? "The bulk operation did not start.", "error");
        return;
      }
      if (res.success && res.operation) {
        toast(res.message, "info");
        navigate(`/system/operations/${res.operation.id}`);
        return;
      }
      // The rows moved since the list was read: confirm the count there is now.
      if (res.matched != null && res.matched > 0 && res.matched !== expected) {
        changed = `The filter now matches ${res.matched.toLocaleString()}, not ${expected.toLocaleString()}. `;
        expected = res.matched;
        continue;
      }
      toast(res.message, "error");
      // Another operation is running on this host: open it.
      if (res.operation) navigate(`/system/operations/${res.operation.id}`);
      return;
    }
  }

  return (
    <span className="flex flex-wrap items-center gap-3 border-l border-accent-line pl-3">
      {unavailable ? (
        <span className="text-muted">{unavailable}</span>
      ) : (
        actions.map((a) => (
          <button
            key={a.kind}
            disabled={busy}
            onClick={() => apply(a)}
            title={`${BULK_KINDS[a.kind].verb} every one of the ${BULK_KINDS[a.kind].noun} this filter matches, not only the selected ones.`}
            className={`font-medium hover:underline disabled:opacity-50 ${a.danger ? "text-danger-fg" : "text-accent-fg"}`}
          >
            {BULK_KINDS[a.kind].verb} all {total.toLocaleString()} matching
          </button>
        ))
      )}
      {askNote && (
        <NotePrompt
          title={askNote.title}
          label="Resolution note (why are these being closed without retry?)"
          confirmLabel="Acknowledge"
          onConfirm={(n) => closeNote(n)}
          onClose={() => closeNote(null)}
        />
      )}
    </span>
  );
}
