import { useState } from "react";
import { useMutation } from "urql";
import { CONFIGURE_EFFECT } from "../graphql/mutations";
import { Modal } from "./Modal";
import { Spinner } from "./Spinner";
import {
  changedValues,
  editableFields,
  errorsByField,
  formatSettingLabel,
  initialFormValues,
  setInCodeFields,
} from "../lib/effectSettings";
import { shortName } from "../lib/format";
import { toast } from "../lib/toast";
import type { ConfigureEffectResponse, EffectInfo, EffectSettingInfo } from "../types";

const INPUT =
  "w-full text-sm border border-line-strong rounded-md px-2 py-1 bg-field";

/**
 * Edits a configurable effect's settings through configureEffect, as the Blazor
 * ConfigureEffectDialog does: a switch for a boolean, a dropdown for an enum, text read as the
 * setting's type otherwise, and a setting set in code shown but not editable. A sensitive setting
 * is never shown: its field opens blank, says whether a value is set, and is sent only when given a
 * new one. Save sends only the settings that changed, all or none; a refusal keeps the dialog open
 * with the reason beside each refused setting. The change is in memory in the API process and
 * applies to its next run.
 */
export function ConfigureEffectDialog({
  effect,
  onClose,
  onSaved,
}: {
  effect: EffectInfo;
  onClose: () => void;
  onSaved: () => void;
}) {
  const fields = effect.fields ?? [];
  const editable = editableFields(fields);
  const setInCode = setInCodeFields(fields);
  const [opened] = useState(() => initialFormValues(fields));
  const [values, setValues] = useState(opened);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [, configure] = useMutation(CONFIGURE_EFFECT);

  const set = (name: string, value: string) => setValues((v) => ({ ...v, [name]: value }));

  async function save() {
    setError(null);
    setFieldErrors({});
    const changed = changedValues(fields, opened, values);
    if (changed.length === 0) {
      onClose();
      return;
    }
    setBusy(true);
    const r = await configure({ fullName: effect.fullName, values: changed });
    setBusy(false);
    const res: ConfigureEffectResponse | undefined = r.data?.operations?.configureEffect;
    if (r.error || !res) {
      setError(r.error?.message ?? "The configuration was not saved.");
      return;
    }
    if (!res.success) {
      setError(res.message);
      setFieldErrors(errorsByField(res.errors));
      return;
    }
    toast(res.message, "success");
    onSaved();
    onClose();
  }

  return (
    <Modal title={`Configure ${effect.name}`} onClose={onClose}>
      <p className="text-sm text-fg-2 mb-4">
        Configuration:{" "}
        <strong>{effect.configurationTypeName ? shortName(effect.configurationTypeName) : effect.name}</strong>
      </p>

      {fields.length === 0 && (
        <p className="text-sm text-muted mb-4">This effect has no settings to edit here.</p>
      )}

      <div className="space-y-3">
        {editable.map((f) => (
          <Setting
            key={f.name}
            field={f}
            value={values[f.name] ?? ""}
            error={fieldErrors[f.name]}
            onChange={(v) => set(f.name, v)}
          />
        ))}
        {setInCode.map((f) => (
          <div
            key={f.name}
            data-testid={`set-in-code-${f.name}`}
            className="flex items-center justify-between gap-4 text-sm"
          >
            <span className="text-fg-2">{formatSettingLabel(f.name)}</span>
            <span className="text-xs text-muted">
              {f.hasValue ? "Set in code" : "Not set"} (not editable here)
            </span>
          </div>
        ))}
      </div>

      {error && (
        <p role="alert" className="mt-4 text-sm text-danger-fg">
          {error}
        </p>
      )}

      <div className="mt-5 flex justify-end gap-2">
        <button
          onClick={onClose}
          className="px-3 py-2 rounded-lg text-sm text-fg-2 hover:bg-hover"
        >
          Cancel
        </button>
        <button
          onClick={save}
          disabled={busy || fields.length === 0}
          className="px-3 py-2 rounded-lg text-sm font-medium bg-accent text-on-accent hover:bg-accent-hover disabled:opacity-50 inline-flex items-center gap-1.5"
        >
          {busy && <Spinner />}
          {busy ? "Saving…" : "Save"}
        </button>
      </div>
    </Modal>
  );
}

function Setting({
  field: f,
  value,
  error,
  onChange,
}: {
  field: EffectSettingInfo;
  value: string;
  error: string | undefined;
  onChange: (value: string) => void;
}) {
  const label = formatSettingLabel(f.name);
  const errorText = error && (
    <p className="text-xs text-danger-fg mt-1" data-testid={`setting-error-${f.name}`}>
      {error}
    </p>
  );

  if (f.sensitive)
    return (
      <div data-testid={`sensitive-${f.name}`}>
        <label className="block text-xs text-muted mb-1">
          {label}
          <input
            type="password"
            autoComplete="new-password"
            aria-label={label}
            value={value}
            onChange={(e) => onChange(e.target.value)}
            placeholder={f.hasValue ? "Set a new value" : "Not set; enter a value"}
            className={`${INPUT} mt-1`}
          />
        </label>
        <p className="text-xs text-muted">
          Sensitive: {f.hasValue ? "a value is set, and it is never shown." : "no value is set."} Leave blank to
          keep it as it is.
        </p>
        {errorText}
      </div>
    );

  if (f.kind === "BOOLEAN")
    return (
      <div>
        <label className="flex items-center justify-between gap-4 text-sm rounded-md border border-line px-3 py-2">
          <span className="text-fg-2">{label}</span>
          <input
            type="checkbox"
            role="switch"
            aria-label={label}
            checked={value === "true"}
            onChange={(e) => onChange(e.target.checked ? "true" : "false")}
          />
        </label>
        {errorText}
      </div>
    );

  if (f.kind === "ENUM")
    return (
      <div>
        <label className="block text-xs text-muted">
          {label}
          <select aria-label={label} value={value} onChange={(e) => onChange(e.target.value)} className={`${INPUT} mt-1`}>
            {(f.nullable || value === "") && <option value="">{f.nullable ? "(none)" : "Select…"}</option>}
            {(f.enumValues ?? []).map((v) => (
              <option key={v} value={v}>
                {v}
              </option>
            ))}
          </select>
        </label>
        {errorText}
      </div>
    );

  return (
    <div>
      <label className="block text-xs text-muted">
        {label}
        <input
          aria-label={label}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={f.hint}
          title={`${f.typeName}${f.nullable ? " (blank for none)" : ""}`}
          className={`${INPUT} mt-1`}
        />
      </label>
      {errorText}
    </div>
  );
}
