import type { AdvanceResult, RehydrationResult } from './results';

/** One normalized shape for any declined action or failed load — so a UI reasons over one mental model. */
export interface Problem {
  kind: 'rejected' | 'load-error';
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
