import { useState } from "react";
import { Modal } from "./Modal";
import { Spinner } from "./Spinner";

// Note-capturing dialog used for dead-letter acknowledgement (single and bulk). Replaces
// window.prompt so the note is required and the flow is keyboard-friendly. The scheduler refuses
// an acknowledgement note over 1,000 characters, so the box stops there, as the Blazor one does.
const MAX_NOTE_LENGTH = 1000;
export function NotePrompt({
  title,
  label,
  confirmLabel = "Confirm",
  busy = false,
  onConfirm,
  onClose,
}: {
  title: string;
  label: string;
  confirmLabel?: string;
  busy?: boolean;
  onConfirm: (note: string) => void;
  onClose: () => void;
}) {
  const [note, setNote] = useState("");
  const trimmed = note.trim();

  return (
    <Modal title={title} onClose={onClose}>
      <label className="block text-sm text-fg-2 mb-2">
        {label}
      </label>
      <textarea
        autoFocus
        value={note}
        onChange={(e) => setNote(e.target.value)}
        rows={3}
        maxLength={MAX_NOTE_LENGTH}
        className="w-full rounded-lg border border-line-strong bg-field px-3 py-2 text-sm text-fg focus:outline-none focus:ring-2 focus:ring-accent"
        placeholder="Reason / note…"
      />
      <div className="mt-4 flex justify-end gap-2">
        <button
          onClick={onClose}
          className="px-3 py-2 rounded-lg text-sm text-fg-2 hover:bg-hover"
        >
          Cancel
        </button>
        <button
          disabled={!trimmed || busy}
          onClick={() => onConfirm(trimmed)}
          className="px-3 py-2 rounded-lg text-sm font-medium bg-accent text-on-accent hover:bg-accent-hover disabled:opacity-50 inline-flex items-center gap-1.5"
        >
          {busy && <Spinner />}
          {busy ? "Working…" : confirmLabel}
        </button>
      </div>
    </Modal>
  );
}
