// AUTO-GENERATED from checkout.ir.json by generateContextTypes. Do not edit by hand.

export type CheckoutState = "Cart" | "Paid" | "Review";
export type CheckoutTrigger = "Back" | "Next" | "Pay" | "Restart";

export type CheckoutCartContext = {
  currency: string;
  items: string[];
  receipt?: string | null;
  total: number;
};

export type CheckoutPaidContext = {
  currency: string;
  items: string[];
  receipt: string;
  total: number;
};

export type CheckoutReviewContext = {
  currency: string;
  items: string[];
  receipt?: string | null;
  total: number;
};

export type CheckoutContexts = {
  Cart: CheckoutCartContext;
  Paid: CheckoutPaidContext;
  Review: CheckoutReviewContext;
};

export type CheckoutPayInput = {
  receipt: string;
};

export type CheckoutInputs = {
  Back: undefined;
  Next: undefined;
  Pay: CheckoutPayInput;
  Restart: undefined;
};

export type CheckoutSpec = {
  states: CheckoutContexts;
  triggers: CheckoutInputs;
};
