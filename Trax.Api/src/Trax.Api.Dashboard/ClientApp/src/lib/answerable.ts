import { useConsoleClient, type ConsoleClient } from "./consoleClient";

// Which queries the client behind a page can answer. A live API and the mock answer every one. The
// demo answers only what its recordings hold and never invents data, so a page part whose read was
// never recorded (a run graph, the state machines) is not offered there until the recorder records
// it, rather than shown failing.

/** Limits what `client` is said to answer to the named query operations. */
export function limitAnswers(client: ConsoleClient, operations: Iterable<string>): void {
  client.limitAnswers(operations);
}

/** True when `client` answers the query operation named `operation`. */
export function answers(client: ConsoleClient, operation: string): boolean {
  return client.answers(operation);
}

/** True when the client in context answers the query operation named `operation`. */
export function useAnswers(operation: string): boolean {
  return answers(useConsoleClient(), operation);
}
