import { useState, useSyncExternalStore } from "react";
import { answerConfirm, getConfirm, subscribeConfirm, type ConfirmRequest } from "../lib/confirm";
import { Modal } from "./Modal";

/** The dialog behind lib/confirm.ts. Mounted once, in the layout. */
export function ConfirmHost() {
  const request = useSyncExternalStore(subscribeConfirm, getConfirm, getConfirm);
  if (!request) return null;
  // Keyed by the request, so a new one starts with an empty answer.
  return <ConfirmDialog key={`${request.title}\u0000${request.message}`} request={request} />;
}

function ConfirmDialog({ request }: { request: ConfirmRequest }) {
  const [typed, setTyped] = useState("");
  const needs = request.typeToConfirm;
  const ready = !needs || typed.trim() === needs;
  const label = request.confirmLabel ?? request.title;
  return (
    <Modal title={request.title} onClose={() => answerConfirm(false)}>
      <form
        onSubmit={(e) => {
          e.preventDefault();
          if (ready) answerConfirm(true);
        }}
      >
        <p className="text-sm text-fg-2">{request.message}</p>
        {needs && (
          <label className="mt-4 block text-sm text-fg-2">
            Type <span className="font-mono font-semibold text-fg">{needs}</span> to confirm
            <input
              autoFocus
              value={typed}
              onChange={(e) => setTyped(e.target.value)}
              className="mt-1 w-full rounded-md border border-line bg-field px-3 py-1.5 font-mono text-sm text-fg"
              aria-label={`Type ${needs} to confirm`}
            />
          </label>
        )}
        <div className="mt-5 flex justify-end gap-2">
          <button
            type="button"
            onClick={() => answerConfirm(false)}
            className="rounded-md px-3 py-1.5 text-sm text-fg-2 hover:bg-hover"
          >
            Keep it
          </button>
          <button
            type="submit"
            autoFocus={!needs}
            disabled={!ready}
            className={`rounded-md px-3 py-1.5 text-sm font-medium disabled:opacity-50 ${
              request.danger ? "bg-warn text-on-warn hover:bg-warn-hover" : "bg-accent text-on-accent hover:bg-accent-hover"
            }`}
          >
            {label}
          </button>
        </div>
      </form>
    </Modal>
  );
}
