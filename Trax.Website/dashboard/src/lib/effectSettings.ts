import type { EffectSettingError, EffectSettingInfo } from "../types";

// The Configure dialog's form logic, as the Blazor ConfigureEffectDialog keeps it: one text value
// per editable setting, sent only when it changed, so a value saved from elsewhere while the dialog
// was open is not reverted. A sensitive setting is never read back: its field opens blank, and it is
// sent only when the operator types a new value.

export type EffectFormValues = Record<string, string>;

export interface EffectSettingValue {
  name: string;
  value: string | null;
}

/** The settings the dialog can write: everything but those set in code. */
export function editableFields(fields: EffectSettingInfo[]): EffectSettingInfo[] {
  return fields.filter((f) => f.kind !== "SET_IN_CODE");
}

/** The settings shown read-only: a delegate, collection or object set in code. */
export function setInCodeFields(fields: EffectSettingInfo[]): EffectSettingInfo[] {
  return fields.filter((f) => f.kind === "SET_IN_CODE");
}

/** The values the form opens with: each setting's current text, blank for a sensitive one. */
export function initialFormValues(fields: EffectSettingInfo[]): EffectFormValues {
  return Object.fromEntries(
    editableFields(fields).map((f) => [f.name, f.sensitive ? "" : (f.value ?? "")]),
  );
}

/**
 * The values to send: each setting whose text differs from what the form opened with, and each
 * sensitive setting given a new value. A blank value goes as "", which the API reads as no value
 * for a setting that accepts none and refuses for one that needs one.
 */
export function changedValues(
  fields: EffectSettingInfo[],
  opened: EffectFormValues,
  current: EffectFormValues,
): EffectSettingValue[] {
  return editableFields(fields)
    .filter((f) =>
      f.sensitive ? (current[f.name] ?? "") !== "" : (current[f.name] ?? "") !== (opened[f.name] ?? ""),
    )
    .map((f) => ({ name: f.name, value: current[f.name] ?? "" }));
}

/** Each refused setting's message, by setting name. */
export function errorsByField(errors: EffectSettingError[]): Record<string, string> {
  return Object.fromEntries(errors.map((e) => [e.field, e.message]));
}

/** "MaxParameterBytes" -> "Max Parameter Bytes", as the Blazor dialog labels a setting. */
export function formatSettingLabel(name: string): string {
  return name.replace(/(?<=[a-z0-9])(?=[A-Z])/g, " ");
}
