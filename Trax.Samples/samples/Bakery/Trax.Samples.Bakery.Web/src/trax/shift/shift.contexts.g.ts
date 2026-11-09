// AUTO-GENERATED from shift.ir.json by generateContextTypes. Do not edit by hand.

export type ShiftState = "Off" | "On" | "Oven" | "Over";
export type ShiftTrigger = "ClockIn" | "ClockOut" | "FireOven";

export type ShiftOffContext = Record<string, never>;

export type ShiftOnContext = Record<string, never>;

export type ShiftOvenContext = Record<string, never>;

export type ShiftOverContext = Record<string, never>;

export type ShiftContexts = {
  Off: ShiftOffContext;
  On: ShiftOnContext;
  Oven: ShiftOvenContext;
  Over: ShiftOverContext;
};

export type ShiftInputs = {
  ClockIn: undefined;
  ClockOut: undefined;
  FireOven: undefined;
};

export type ShiftSpec = {
  states: ShiftContexts;
  triggers: ShiftInputs;
};
