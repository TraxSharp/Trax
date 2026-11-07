// AUTO-GENERATED from order.ir.json by generateContextTypes. Do not edit by hand.

export type OrderState = "Baking" | "Declined" | "Draft" | "Paying" | "Placed" | "Unpaid";
export type OrderTrigger = "Bake" | "Order" | "Place";
export type OrderOutcome = "Paying.cancelled" | "Paying.done" | "Paying.failed";

export type OrderBakingContext = {
  approved?: boolean | null;
  loaves: number;
};

export type OrderDeclinedContext = {
  approved?: boolean | null;
  loaves: number;
};

export type OrderDraftContext = {
  approved?: boolean | null;
  loaves: number;
};

export type OrderPayingContext = {
  approved?: boolean | null;
  loaves: number;
};

export type OrderPlacedContext = {
  approved?: boolean | null;
  loaves: number;
};

export type OrderUnpaidContext = {
  approved?: boolean | null;
  loaves: number;
};

export type OrderContexts = {
  Baking: OrderBakingContext;
  Declined: OrderDeclinedContext;
  Draft: OrderDraftContext;
  Paying: OrderPayingContext;
  Placed: OrderPlacedContext;
  Unpaid: OrderUnpaidContext;
};

export type OrderOrderInput = {
  loaves: number;
};

export type OrderPayingDoneOutput = {
  approved: boolean;
};

export type OrderInputs = {
  Bake: undefined;
  Order: OrderOrderInput;
  Place: undefined;
  "Paying.cancelled": undefined;
  "Paying.done": OrderPayingDoneOutput;
  "Paying.failed": undefined;
};

export type OrderSpec = {
  states: OrderContexts;
  triggers: OrderInputs;
};
