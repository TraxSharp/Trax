// The gauge twin, generated from the IR the C# test machine (GaugeMachine) exports. Its guards compare with lt,
// gte and lte, and its corpus samples each on both sides of its bound, so the two runtimes are held to the same
// comparisons.

import { gauge } from "./gauge.machine.g";

export { gauge };
export type { GaugeSpec, GaugeState, GaugeTrigger } from "./gauge.contexts.g";

export const gaugeCore = gauge.core;
