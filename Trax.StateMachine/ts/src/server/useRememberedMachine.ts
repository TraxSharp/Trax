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
 * coming back answers with the instance the first start created. A `rememberKey` that changes opens that key's
 * instance in the same way.
 *
 * An instance the stream says was deleted or expired is forgotten and a new one started. One the server answers
 * `NOT_FOUND` stops the hook (`error.code` `GONE`, `error.deleted` false) and stays remembered: the server answers a
 * missing instance and another caller's alike, and forgetting another caller's would leave it on the server for good.
 * Keep each user's instance under a key of its own, and call `forget()` to start over.
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

  const pendingStart = useCallback(
    (key: string): string => {
      const kept = read(`${key}:start`);
      if (kept) return kept;
      const requestId = mint();
      write(`${key}:start`, requestId);
      return requestId;
    },
    [mint],
  );

  const openingFor = (key: string): Opening => {
    const id = read(key);
    return id ? { id } : { startRequestId: pendingStart(key) };
  };

  const [held, setHeld] = useState<{ key: string; opening: Opening }>(() => ({
    key: rememberKey,
    opening: openingFor(rememberKey),
  }));
  // A new key opens its own instance in this render, so nothing of the old key's is sent under it.
  let opening = held.opening;
  if (held.key !== rememberKey) {
    opening = openingFor(rememberKey);
    setHeld({ key: rememberKey, opening });
  }

  const result = useServerMachine(machine, {
    ...rest,
    id: opening.id,
    startRequestId: opening.startRequestId,
    startStorageKey: null,
  });

  // In the first render after the key changed, the result is still the old key's instance: it is neither remembered
  // nor forgotten under the new key.
  const committedKey = useRef(rememberKey);
  const current = committedKey.current === rememberKey;

  // The instance last forgotten: its id can still show for a render after, and is never remembered again.
  const dropped = useRef<string | null>(null);

  const forget = useCallback(() => {
    dropped.current = read(rememberKey) ?? dropped.current;
    write(rememberKey, null);
    write(startKey, null);
    setHeld({ key: rememberKey, opening: { startRequestId: pendingStart(rememberKey) } });
  }, [rememberKey, startKey, pendingStart]);

  // A started instance is remembered, and its start is no longer pending.
  useEffect(() => {
    if (!current) return;
    if (result.id && opening.id === undefined && result.id !== dropped.current) {
      write(rememberKey, result.id);
      write(startKey, null);
    }
  }, [current, result.id, opening.id, rememberKey, startKey]);

  // An instance the stream says was deleted is forgotten, and a new one started, once.
  const forgotten = useRef<string | null>(null);
  useEffect(() => {
    if (!current) return;
    if (result.error?.code !== ClientProblemCode.Gone || !result.error.deleted) return;
    if (forgotten.current === result.id) return;
    forgotten.current = result.id;
    dropped.current = result.id;
    forget();
  }, [current, result.error, result.id, forget]);

  useEffect(() => {
    committedKey.current = rememberKey;
  }, [rememberKey]);

  return { ...result, forget };
}
