// AUTO-GENERATED from shift.ir.json by generateContextTypes. Do not edit by hand.

export type ShiftState = "Off" | "On";
export type ShiftTrigger = "ClockIn";

export type ShiftOffContext = Record<string, never>;

export type ShiftOnContext = Record<string, never>;

export type ShiftContexts = {
  Off: ShiftOffContext;
  On: ShiftOnContext;
};

export type ShiftInputs = {
  ClockIn: undefined;
};

export type ShiftSpec = {
  states: ShiftContexts;
  triggers: ShiftInputs;
};
