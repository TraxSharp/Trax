/**
 * `@traxsharp/state-machine/react`: the hook that drives one instance of a machine the server owns. The server applies
 * every transition; the twin only predicts it.
 */
export {
  useServerMachine,
  type UseServerMachineOptions,
  type UseServerMachineResult,
} from './server/useServerMachine';

export {
  useRememberedMachine,
  type UseRememberedMachineOptions,
  type UseRememberedMachineResult,
} from './server/useRememberedMachine';
