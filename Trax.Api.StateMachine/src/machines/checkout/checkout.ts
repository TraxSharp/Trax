// The checkout twin, now sourced ENTIRELY from the generated modules. The hand-written guards, reducers, and
// context validators are gone: the types come from checkout.contexts.g and the runnable, typed machine from
// checkout.machine.g, both generated from the IR the C# source (DeclarativeCheckout) exports. checkoutCore and
// checkoutDefinition are derived from that machine for the differential oracle and the conformance tests.

import { checkout } from "./checkout.machine.g";

export { checkout };
export type {
  CheckoutSpec,
  CheckoutState,
  CheckoutTrigger,
} from "./checkout.contexts.g";

export const checkoutCore = checkout.core;
export const checkoutDefinition = checkoutCore.definition;
