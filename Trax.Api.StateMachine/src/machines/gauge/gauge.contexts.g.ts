// AUTO-GENERATED from gauge.ir.json by generateContextTypes. Do not edit by hand.

export type GaugeState = "Idle" | "Reading";
export type GaugeTrigger = "Lower" | "Raise" | "Settle";

export type GaugeIdleContext = Record<string, never>;

export type GaugeReadingContext = {
  reading: number;
};

export type GaugeContexts = {
  Idle: GaugeIdleContext;
  Reading: GaugeReadingContext;
};

export type GaugeLowerInput = {
  value: number;
};

export type GaugeRaiseInput = {
  value: number;
};

export type GaugeInputs = {
  Lower: GaugeLowerInput;
  Raise: GaugeRaiseInput;
  Settle: undefined;
};

export type GaugeSpec = {
  states: GaugeContexts;
  triggers: GaugeInputs;
};
