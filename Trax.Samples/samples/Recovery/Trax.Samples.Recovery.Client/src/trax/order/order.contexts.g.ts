// AUTO-GENERATED from order.ir.json by generateContextTypes. Do not edit by hand.

export type OrderState = "Cancelled" | "Cart" | "Cleared" | "Delivered" | "Ordered";
export type OrderTrigger = "AddItem" | "PlaceOrder";
export type OrderOutcome = "Ordered.cancelled" | "Ordered.done" | "Ordered.failed";

export type OrderCancelledContext = {
  items?: string[] | null;
  total?: number | null;
};

export type OrderCartContext = {
  items?: string[] | null;
  total?: number | null;
};

export type OrderClearedContext = {
  items?: string[] | null;
  total?: number | null;
};

export type OrderDeliveredContext = {
  items?: string[] | null;
  total?: number | null;
};

export type OrderOrderedContext = {
  items?: string[] | null;
  total?: number | null;
};

export type OrderContexts = {
  Cancelled: OrderCancelledContext;
  Cart: OrderCartContext;
  Cleared: OrderClearedContext;
  Delivered: OrderDeliveredContext;
  Ordered: OrderOrderedContext;
};

export type OrderAddItemInput = {
  item: string;
};

export type OrderOrderedDoneOutput = {
  courier: string;
  orderId: string;
};

export type OrderInputs = {
  AddItem: OrderAddItemInput;
  PlaceOrder: undefined;
  "Ordered.cancelled": undefined;
  "Ordered.done": OrderOrderedDoneOutput;
  "Ordered.failed": undefined;
};

export type OrderSpec = {
  states: OrderContexts;
  triggers: OrderInputs;
};
