// The turnstile twin, now sourced ENTIRELY from the generated modules. There are no hand-written guards,
// reducers, or context validators here anymore: the types come from turnstile.contexts.g and the runnable,
// typed machine from turnstile.machine.g, both generated from the IR the C# source exports. turnstileCore and
// turnstileDefinition are derived from that machine for the differential oracle and the migration test.

import { turnstile } from "./turnstile.machine.g";

export { turnstile };
export type { TurnstileSpec, TurnstileState, TurnstileTrigger } from "./turnstile.contexts.g";

export const turnstileCore = turnstile.core;
export const turnstileDefinition = turnstileCore.definition;
