// AUTO-GENERATED from checkout.ir.json by generateContextTypes. Do not edit by hand.

export type CheckoutState = "Abandoned" | "Address" | "Cart" | "Delivered" | "Failed" | "Payment" | "Placed" | "Placing" | "Shipping";
export type CheckoutTrigger = "Back" | "ChoosePayment" | "PlaceOrder" | "Retry" | "SetAddress" | "SetLines" | "StartOver" | "Track";
export type CheckoutOutcome = "Placing.cancelled" | "Placing.done" | "Placing.failed" | "Shipping.cancelled" | "Shipping.done" | "Shipping.failed";

export type CheckoutAbandonedContext = {
  address?: string | null;
  lines?: unknown[] | null;
  paymentToken?: string | null;
};

export type CheckoutAddressContext = {
  address?: string | null;
  lines?: unknown[] | null;
  paymentToken?: string | null;
};

export type CheckoutCartContext = {
  address?: string | null;
  lines?: unknown[] | null;
  paymentToken?: string | null;
};

export type CheckoutDeliveredContext = {
  address?: string | null;
  lines?: unknown[] | null;
  orderId?: string | null;
  parcelId?: string | null;
  paymentToken?: string | null;
};

export type CheckoutFailedContext = {
  address?: string | null;
  lines?: unknown[] | null;
  paymentToken?: string | null;
};

export type CheckoutPaymentContext = {
  address?: string | null;
  lines?: unknown[] | null;
  paymentToken?: string | null;
};

export type CheckoutPlacedContext = {
  address?: string | null;
  lines?: unknown[] | null;
  orderId?: string | null;
  paymentToken?: string | null;
};

export type CheckoutPlacingContext = {
  address?: string | null;
  lines?: unknown[] | null;
  paymentToken?: string | null;
};

export type CheckoutShippingContext = {
  address?: string | null;
  lines?: unknown[] | null;
  orderId?: string | null;
  parcelId?: string | null;
  paymentToken?: string | null;
};

export type CheckoutContexts = {
  Abandoned: CheckoutAbandonedContext;
  Address: CheckoutAddressContext;
  Cart: CheckoutCartContext;
  Delivered: CheckoutDeliveredContext;
  Failed: CheckoutFailedContext;
  Payment: CheckoutPaymentContext;
  Placed: CheckoutPlacedContext;
  Placing: CheckoutPlacingContext;
  Shipping: CheckoutShippingContext;
};

export type CheckoutChoosePaymentInput = {
  paymentToken: string;
};

export type CheckoutSetAddressInput = {
  address: string;
};

export type CheckoutSetLinesInput = {
  lines: { quantity: number; sku: string; }[];
};

export type CheckoutPlacingDoneOutput = {
  orderId: string;
  total: number;
  transactionId: string;
};

export type CheckoutInputs = {
  Back: undefined;
  ChoosePayment: CheckoutChoosePaymentInput;
  PlaceOrder: undefined;
  Retry: undefined;
  SetAddress: CheckoutSetAddressInput;
  SetLines: CheckoutSetLinesInput;
  StartOver: undefined;
  Track: undefined;
  "Placing.cancelled": undefined;
  "Placing.done": CheckoutPlacingDoneOutput;
  "Placing.failed": undefined;
  "Shipping.cancelled": undefined;
  "Shipping.done": undefined;
  "Shipping.failed": undefined;
};

export type CheckoutSpec = {
  states: CheckoutContexts;
  triggers: CheckoutInputs;
};
