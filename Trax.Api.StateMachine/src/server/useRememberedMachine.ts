import { useCallback, useEffect, useRef, useState } from 'react';
import { ClientProblemCode } from '../results';
import type { MachineSpec, TypedMachine } from '../typed';
import { useServerMachine, type UseServerMachineOptions, type UseServerMachineResult } from './useServerMachine';

/** What {@link useRememberedMachine} takes: {@link useServerMachine}'s options, less the instance it chooses itself. */
export interface UseRememberedMachineOptions
  extends Omit<UseServerMachineOptions, 'id' | 'startRequestId' | 'startStorageKey'> {
  /**
   * Where this browser keeps the instance, in `localStorage`: the instance id under this key, and the requestId of a
   * start that has not answered under `<key>:start`. Give each machine a page keeps its own key.
   */
  rememberKey: string;
}

/** What {@link useRememberedMachine} renders from: {@link useServerMachine}'s result and a way to start over. */
export interface UseRememberedMachineResult<Spec extends MachineSpec> extends UseServerMachineResult<Spec> {
  /** Forgets the remembered instance and starts a new one. The old one stays on the server. */
  forget(): void;
}

// localStorage, guarded: it can be missing (no browser) or throw (a private window, blocked site data).
function read(key: string): string | null {
  try {
    return globalThis.localStorage?.getItem(key) ?? null;
  } catch {
    return null;
  }
}

function write(key: string, value: string | null): void {
  try {
    if (value === null) globalThis.localStorage?.removeItem(key);
    else globalThis.localStorage?.setItem(key, value);
  } catch {
    // Nothing is kept between visits; this one still works.
  }
}

interface Opening {
  id?: string;
  startRequestId?: string;
}

/**
 * One instance of a machine the server owns that this browser keeps across visits: it opens the instance it
 * remembered, or starts one under a requestId it keeps until the start answers, so closing the page mid-start and
 * coming back answers with the instance the first start created. An instance the server no longer holds (deleted
 * elsewhere, or expired) is forgotten and a new one started.
 * @see https://traxsharp.net/docs/sdk-reference/statemachine-api/typescript-package#talking-to-a-server
 */
export function useRememberedMachine<Spec extends MachineSpec>(
  machine: TypedMachine<Spec>,
  options: UseRememberedMachineOptions,
): UseRememberedMachineResult<Spec> {
  const { rememberKey, ...rest } = options;
  const startKey = `${rememberKey}:start`;
  const mint = useCallback(
    () => (rest.newRequestId ?? (() => globalThis.crypto.randomUUID()))(),
    [rest.newRequestId],
  );

  const pendingStart = useCallback((): string => {
    const kept = read(startKey);
    if (kept) return kept;
    const requestId = mint();
    write(startKey, requestId);
    return requestId;
  }, [startKey, mint]);

  const [opening, setOpening] = useState<Opening>(() => {
    const id = read(rememberKey);
    return id ? { id } : { startRequestId: pendingStart() };
  });

  const result = useServerMachine(machine, {
    ...rest,
    id: opening.id,
    startRequestId: opening.startRequestId,
    startStorageKey: null,
  });

  // The instance last forgotten: its id can still show for a render after, and is never remembered again.
  const dropped = useRef<string | null>(null);

  const forget = useCallback(() => {
    dropped.current = read(rememberKey) ?? dropped.current;
    write(rememberKey, null);
    write(startKey, null);
    setOpening({ startRequestId: pendingStart() });
  }, [rememberKey, startKey, pendingStart]);

  // A started instance is remembered, and its start is no longer pending.
  useEffect(() => {
    if (result.id && opening.id === undefined && result.id !== dropped.current) {
      write(rememberKey, result.id);
      write(startKey, null);
    }
  }, [result.id, opening.id, rememberKey, startKey]);

  // An instance the server no longer holds is forgotten, and a new one started, once.
  const forgotten = useRef<string | null>(null);
  useEffect(() => {
    if (result.error?.code !== ClientProblemCode.Gone || forgotten.current === result.id) return;
    forgotten.current = result.id;
    dropped.current = result.id;
    forget();
  }, [result.error, result.id, forget]);

  return { ...result, forget };
}
