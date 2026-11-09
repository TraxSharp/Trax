// AUTO-GENERATED from desk.ir.json by generateContextTypes. Do not edit by hand.

export type DeskState = "Closing" | "Done" | "Open" | "Waiting";
export type DeskTrigger = "CloseGate" | "OpenDesk";

export type DeskClosingContext = Record<string, never>;

export type DeskDoneContext = Record<string, never>;

export type DeskOpenContext = Record<string, never>;

export type DeskWaitingContext = Record<string, never>;

export type DeskContexts = {
  Closing: DeskClosingContext;
  Done: DeskDoneContext;
  Open: DeskOpenContext;
  Waiting: DeskWaitingContext;
};

export type DeskInputs = {
  CloseGate: undefined;
  OpenDesk: undefined;
};

export type DeskSpec = {
  states: DeskContexts;
  triggers: DeskInputs;
};
