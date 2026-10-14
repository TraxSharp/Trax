import { useState } from "react";
import { useMutation } from "urql";
import { UPLOAD_PERSISTED_OPERATION } from "../graphql/mutations";
import { Modal } from "./Modal";
import { Spinner } from "./Spinner";
import type { PersistedOperationError, PersistedOperationPayload } from "../types";

const ERROR_TITLE: Record<string, string> = {
  INVALID_INPUT: "Invalid input",
  PARSE_FAILED: "Parse error",
  SCHEMA_VALIDATION_FAILED: "Schema validation failed",
  SHAPE_DIFF_VIOLATION: "Shape change rejected",
};

function abbreviate(fingerprint: string): string {
  return fingerprint.length > 16 ? `${fingerprint.slice(0, 16)}…` : fingerprint;
}

export interface ExistingPersistedOperation {
  id: string;
  tenantKey: string | null;
  document: string;
  description: string | null;
  version: number;
}

/**
 * Upload a persisted operation, or edit one (`existing`). Mirrors the Blazor
 * PersistedOperationEditor: the server validates the document against the live schema and rejects
 * a shape-changing edit unless "Bypass shape-diff guardrail" is ticked; each refusal is listed
 * with its code, location and, for a shape change, the old and new fingerprints.
 */
export function PersistedOperationEditor({
  existing,
  onSaved,
  onClose,
}: {
  existing?: ExistingPersistedOperation;
  onSaved: () => void;
  onClose: () => void;
}) {
  const [, upload] = useMutation(UPLOAD_PERSISTED_OPERATION);
  const [id, setId] = useState(existing?.id ?? "");
  const [tenantKey, setTenantKey] = useState(existing?.tenantKey ?? "");
  const [description, setDescription] = useState(existing?.description ?? "");
  const [version, setVersion] = useState(String(existing?.version ?? 0));
  const [doc, setDoc] = useState(existing?.document ?? "");
  const [bypass, setBypass] = useState(false);
  const [errors, setErrors] = useState<PersistedOperationError[]>([]);
  const [otherError, setOtherError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function save() {
    setErrors([]);
    setOtherError(null);
    setBusy(true);
    const r = await upload({
      input: {
        id: id.trim(),
        document: doc,
        description: description.trim() ? description : null,
        bypassShapeDiff: bypass,
        version: Number(version) || 0,
        tenantKey: tenantKey.trim() ? tenantKey.trim() : null,
      },
    });
    setBusy(false);
    const payload: PersistedOperationPayload | undefined =
      r.data?.operations?.persistedOperations?.uploadPersistedOperation;
    if (r.error) setOtherError(r.error.message);
    else if (payload?.success) {
      onSaved();
      onClose();
    } else if (payload && payload.errors.length > 0) setErrors(payload.errors);
    else setOtherError("The operation could not be saved.");
  }

  const input =
    "w-full text-sm border border-line-strong rounded-md px-2 py-1 bg-field";

  return (
    <Modal title={existing ? `Edit ${existing.id}` : "Upload persisted operation"} onClose={onClose} wide>
      <div className="space-y-3 text-sm">
        {existing ? (
          <div className="text-fg-2">
            <p>
              <strong>Id:</strong> <code>{existing.id}</code>
            </p>
            <p>
              <strong>Tenant:</strong> {existing.tenantKey || "(default)"}
            </p>
          </div>
        ) : (
          <>
            <label className="block">
              <span className="block text-xs text-muted mb-1">Id</span>
              <input value={id} onChange={(e) => setId(e.target.value)} placeholder="e.g. greet.v1" className={input} />
            </label>
            <label className="block">
              <span className="block text-xs text-muted mb-1">
                Tenant (optional, blank for the default tenant)
              </span>
              <input value={tenantKey} onChange={(e) => setTenantKey(e.target.value)} className={input} />
            </label>
          </>
        )}
        <label className="block">
          <span className="block text-xs text-muted mb-1">Description (optional)</span>
          <input value={description} onChange={(e) => setDescription(e.target.value)} className={input} />
        </label>
        <label className="block">
          <span className="block text-xs text-muted mb-1">Version (optional metadata)</span>
          <input
            value={version}
            onChange={(e) => setVersion(e.target.value.replace(/\D/g, ""))}
            className={`${input} w-32`}
          />
        </label>
        <label className="block">
          <span className="block text-xs text-muted mb-1">Document</span>
          <textarea
            aria-label="Document"
            value={doc}
            onChange={(e) => setDoc(e.target.value)}
            rows={12}
            spellCheck={false}
            className={`${input} font-mono`}
          />
        </label>

        {existing && (
          <>
            <label className="flex items-center gap-2">
              <input type="checkbox" checked={bypass} onChange={(e) => setBypass(e.target.checked)} />
              Bypass shape-diff guardrail
            </label>
            {bypass && (
              <p className="rounded-md bg-warn-soft text-warn-fg px-3 py-2 text-xs">
                Bypass only when the shape change is known to be safe for shipped clients. The change
                is recorded in the audit log.
              </p>
            )}
          </>
        )}

        {errors.map((e, i) => (
          <div
            key={i}
            role="alert"
            className="rounded-md bg-danger-soft border border-danger-line text-danger-fg px-3 py-2 text-xs space-y-1"
          >
            <div>
              <strong>{ERROR_TITLE[e.code] ?? e.code}</strong>
              {e.locations && e.locations.length > 0 && (
                <span>
                  {" "}
                  at line {e.locations[0].line}, column {e.locations[0].column}
                </span>
              )}
            </div>
            <div>{e.message}</div>
            {e.oldFingerprint && e.newFingerprint && (
              <>
                <div>
                  Old fingerprint: <code>{abbreviate(e.oldFingerprint)}</code>
                </div>
                <div>
                  New fingerprint: <code>{abbreviate(e.newFingerprint)}</code>
                </div>
                <div>Tick &quot;Bypass shape-diff guardrail&quot; if the change is verified safe.</div>
              </>
            )}
          </div>
        ))}
        {otherError && (
          <p role="alert" className="text-danger-fg">
            {otherError}
          </p>
        )}

        <div className="flex justify-end gap-2 pt-2">
          <button
            onClick={onClose}
            disabled={busy}
            className="px-3 py-2 rounded-lg text-sm text-fg-2 hover:bg-hover disabled:opacity-50"
          >
            Cancel
          </button>
          <button
            onClick={save}
            disabled={busy || (!existing && !id.trim()) || !doc.trim()}
            className="px-3 py-2 rounded-lg text-sm font-medium bg-accent text-on-accent hover:bg-accent-hover disabled:opacity-50 inline-flex items-center gap-1.5"
          >
            {busy && <Spinner />}
            Save
          </button>
        </div>
      </div>
    </Modal>
  );
}

/**
 * Asks for the reason a persisted operation is deactivated (required, for the audit log). Mirrors
 * the Blazor DeactivationReasonDialog.
 */
export function DeactivationReasonDialog({
  busy = false,
  onConfirm,
  onClose,
}: {
  busy?: boolean;
  onConfirm: (reason: string) => void;
  onClose: () => void;
}) {
  const [reason, setReason] = useState("");
  return (
    <Modal title="Deactivate persisted operation" onClose={onClose}>
      <p className="text-sm text-fg-2 mb-3">
        Deactivating a persisted operation makes future requests for its id resolve to{" "}
        <code>null</code>. Provide a reason for the audit log.
      </p>
      <label className="block text-sm">
        <span className="block text-xs text-muted mb-1">Reason</span>
        <input
          autoFocus
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          className="w-full text-sm border border-line-strong rounded-md px-2 py-1 bg-field"
        />
      </label>
      <div className="mt-4 flex justify-end gap-2">
        <button
          onClick={onClose}
          className="px-3 py-2 rounded-lg text-sm text-fg-2 hover:bg-hover"
        >
          Cancel
        </button>
        <button
          disabled={!reason.trim() || busy}
          onClick={() => onConfirm(reason.trim())}
          className="px-3 py-2 rounded-lg text-sm font-medium bg-warn text-on-warn hover:bg-warn-hover disabled:opacity-50"
        >
          Deactivate
        </button>
      </div>
    </Modal>
  );
}
