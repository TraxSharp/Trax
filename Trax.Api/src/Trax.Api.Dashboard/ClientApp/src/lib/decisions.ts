import { useQuery } from "urql";
import { TRAINS_ASKING_DECISIONS } from "../graphql/queries";
import { useAnswers } from "./answerable";

// Ask afresh and replay-on-retry mean something only for a train that asks a decider (api/0043):
// for every other train they are noise. The console offers them where operations.trains says the
// train asks, and wherever it cannot tell (no answer yet, an older host, a train this host does
// not register), so nothing an operator relies on disappears for want of a read.

/** Whether each train the host registers asks a decider, by full name; null until the host has said. */
export function useTrainsAskingDecisions(): ReadonlyMap<string, boolean> | null {
  const answerable = useAnswers("TrainsAskingDecisions");
  const [{ data }] = useQuery<{ operations: { trains: { fullName: string; asksDecisions: boolean }[] } | null }>({
    query: TRAINS_ASKING_DECISIONS,
    pause: !answerable,
  });
  const trains = data?.operations?.trains;
  return trains ? new Map(trains.map((t) => [t.fullName, t.asksDecisions])) : null;
}

/** Whether to offer the decision controls for these trains: when any of them asks, or might. */
export function offersDecisions(asking: ReadonlyMap<string, boolean> | null, trains: Iterable<string>): boolean {
  if (!asking) return true;
  for (const t of trains) if (asking.get(t) !== false) return true;
  return false;
}
