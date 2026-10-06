// Scalar mock factories for the Trax custom scalars. graphql-tools auto-mocks every
// object field, but it needs a factory for each custom scalar or it throws. Values are
// deterministic (no Math.random / wall clock) so stories and snapshot tests are stable.
import type { IMocks } from "@graphql-tools/mock";

// Monotonic id source so mocked rows get distinct, stable ids across a render.
let longSeq = 1000;

// A fixed clock that steps backwards one minute per call, so auto-mocked lists come out
// newest-first and look plausibly time-ordered. 2026-07-07T12:00:00Z.
let clockMs = 1_783_166_400_000;

export const defaultMocks: IMocks = {
  Long: () => longSeq++,
  DateTime: () => new Date((clockMs -= 60_000)).toISOString(),
  Duration: () => "PT5S",
  UUID: () => "00000000-0000-4000-8000-000000000000",
  Any: () => null,
};
