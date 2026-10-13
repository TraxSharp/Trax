import type { AdvanceResult, RehydrationResult } from './results';

/** One normalized shape for any declined action or failed load — so a UI reasons over one mental model. */
export interface Problem {
  /**
   * `rejected`: the machine (the twin's or the server's) declined. `load-error`: a stored snapshot could not be read.
   * `transport`: the server could not be reached, so nothing is known about the outcome.
   */
  kind: 'rejected' | 'load-error' | 'transport';
  code: string;
  message: string;
}

/** A declined `advance` becomes a Problem; a successful transition is `null`. */
export function problemFromAdvance(result: AdvanceResult): Problem | null {
  if (result.outcome === 'transitioned') return null;
  return { kind: 'rejected', code: result.reason, message: result.detail ?? result.reason };
}

/** A failed `rehydrate` becomes a Problem; an ok result is `null`. */
export function problemFromRehydration(result: RehydrationResult): Problem | null {
  if (result.result === 'ok') return null;
  return { kind: 'load-error', code: result.code, message: result.message };
}
