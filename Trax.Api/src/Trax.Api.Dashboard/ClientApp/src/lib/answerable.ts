import type { Client } from "@urql/core";
import { useClient } from "urql";

// Which queries the client behind a page can answer. A live API and the mock answer every one. The
// demo answers only what its recordings hold and never invents data, so a page part whose read was
// never recorded (a run graph, the state machines) is not offered there until the recorder records
// it, rather than shown failing.
const limits = new WeakMap<Client, ReadonlySet<string>>();

/** Limits what `client` is said to answer to the named query operations. */
export function limitAnswers(client: Client, operations: Iterable<string>): void {
  limits.set(client, new Set(operations));
}

/** True when `client` answers the query operation named `operation`. */
export function answers(client: Client, operation: string): boolean {
  return limits.get(client)?.has(operation) ?? true;
}

/** True when the client in context answers the query operation named `operation`. */
export function useAnswers(operation: string): boolean {
  return answers(useClient(), operation);
}
