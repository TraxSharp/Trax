import { describe, expect, test } from "vitest";
import {
  changedValues,
  editableFields,
  errorsByField,
  formatSettingLabel,
  initialFormValues,
  setInCodeFields,
} from "./effectSettings";
import type { EffectSettingInfo } from "../types";

const field = (over: Partial<EffectSettingInfo>): EffectSettingInfo => ({
  name: "X",
  typeName: "String",
  kind: "TEXT",
  nullable: true,
  enumValues: null,
  sensitive: false,
  hasValue: true,
  value: "x",
  hint: "Enter text",
  ...over,
});

const FIELDS = [
  field({ name: "SaveInputs", typeName: "Boolean", kind: "BOOLEAN", nullable: false, value: "true" }),
  field({ name: "Level", typeName: "LogLevel", kind: "ENUM", enumValues: ["Debug", "Information"], value: "Debug" }),
  field({ name: "MaxBytes", typeName: "Int32", kind: "TEXT", value: "1024" }),
  field({ name: "ApiKey", sensitive: true, value: null }),
  field({ name: "ShouldSave", typeName: "Func`2", kind: "SET_IN_CODE", value: null, hasValue: false }),
];

describe("effect settings form", () => {
  test("splits editable settings from those set in code", () => {
    expect(editableFields(FIELDS).map((f) => f.name)).toEqual(["SaveInputs", "Level", "MaxBytes", "ApiKey"]);
    expect(setInCodeFields(FIELDS).map((f) => f.name)).toEqual(["ShouldSave"]);
  });

  test("opens with each value, and a sensitive one blank", () => {
    expect(initialFormValues(FIELDS)).toEqual({ SaveInputs: "true", Level: "Debug", MaxBytes: "1024", ApiKey: "" });
  });

  test("a null value opens as blank", () => {
    expect(initialFormValues([field({ name: "N", value: null })])).toEqual({ N: "" });
  });

  test("sends only the settings that changed", () => {
    const opened = initialFormValues(FIELDS);
    expect(changedValues(FIELDS, opened, opened)).toEqual([]);
    expect(changedValues(FIELDS, opened, { ...opened, MaxBytes: "2048", SaveInputs: "false" })).toEqual([
      { name: "SaveInputs", value: "false" },
      { name: "MaxBytes", value: "2048" },
    ]);
  });

  test("a cleared value is sent blank, for the API to read as no value", () => {
    const opened = initialFormValues(FIELDS);
    expect(changedValues(FIELDS, opened, { ...opened, MaxBytes: "" })).toEqual([{ name: "MaxBytes", value: "" }]);
  });

  test("a sensitive setting is sent only when given a new value", () => {
    const opened = initialFormValues(FIELDS);
    expect(changedValues(FIELDS, opened, { ...opened, ApiKey: "" })).toEqual([]);
    expect(changedValues(FIELDS, opened, { ...opened, ApiKey: "s3cret" })).toEqual([{ name: "ApiKey", value: "s3cret" }]);
  });

  test("a setting set in code is never sent", () => {
    const opened = initialFormValues(FIELDS);
    expect(changedValues(FIELDS, opened, { ...opened, ShouldSave: "x" })).toEqual([]);
  });

  test("maps errors by setting and labels names as words", () => {
    expect(errorsByField([{ field: "MaxBytes", message: "not a number" }])).toEqual({ MaxBytes: "not a number" });
    expect(formatSettingLabel("MaxParameterBytes")).toBe("Max Parameter Bytes");
    expect(formatSettingLabel("Saves2Things")).toBe("Saves2 Things");
  });
});
