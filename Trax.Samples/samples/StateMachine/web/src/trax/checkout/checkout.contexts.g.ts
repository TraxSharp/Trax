// AUTO-GENERATED from checkout.ir.json by generateContextTypes. Do not edit by hand.

export type CheckoutState = "Cart" | "Paid" | "Review";
export type CheckoutTrigger = "AddItem" | "Back" | "Next" | "Pay" | "Reset";

export type CheckoutCartContext = {
  items: string[];
  receipt?: string | null;
  total: number;
};

export type CheckoutPaidContext = {
  items: string[];
  receipt: string;
  total: number;
};

export type CheckoutReviewContext = {
  items: string[];
  receipt?: string | null;
  total: number;
};

export type CheckoutContexts = {
  Cart: CheckoutCartContext;
  Paid: CheckoutPaidContext;
  Review: CheckoutReviewContext;
};

export type CheckoutAddItemInput = {
  item: string;
};

export type CheckoutInputs = {
  AddItem: CheckoutAddItemInput;
  Back: undefined;
  Next: undefined;
  Pay: undefined;
  Reset: undefined;
};

export type CheckoutSpec = {
  states: CheckoutContexts;
  triggers: CheckoutInputs;
};
