import type { MachineObserver } from './controller';
import type { MachineSpec } from './typed';

export interface DevLoggerOptions {
  /** Re-throw on an internal error (a real guard/reducer bug) so dev fails fast instead of degrading silently. */
  strict?: boolean;
  console?: Pick<Console, 'log' | 'warn' | 'error'>;
}

/**
 * A {@link MachineObserver} that logs every transition and rejection, and makes a genuine internal error
 * loud. Nothing is silent: a declined action warns, an engine bug errors (and throws under `strict`).
 */
export function createDevLogger<Spec extends MachineSpec>(name: string, options: DevLoggerOptions = {}): MachineObserver<Spec> {
  const sink = options.console ?? console;
  const tag = `[state-machine:${name}]`;
  return {
    onTransition: (from, trigger, to) => sink.log(`${tag} ${from} --${trigger}--> ${to}`),
    onRejected: (trigger, problem) => sink.warn(`${tag} ✗ ${trigger} rejected (${problem.code}): ${problem.message}`),
    onInternalError: (trigger, detail) => {
      sink.error(`${tag} ⚠ internal error firing '${trigger}': ${detail}`);
      if (options.strict) throw new Error(`${tag} internal error firing '${trigger}': ${detail}`);
    },
  };
}
