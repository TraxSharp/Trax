import { useApolloClient } from "@apollo/client";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { persistedSocket, subscribePersisted } from "../apollo";
import { ORDER_SCENARIOS, type OrderScenarioKey } from "../cases";
import {
  DELIVERY_RUNS,
  EXECUTION,
  JUNCTION_RUNS,
  ON_JUNCTION_EVENT,
  ORDER_ADD_ITEM,
  ORDER_DISCARD,
  ORDER_PLACE,
  ORDER_START,
  ORDER_TROUBLE,
  TABLET_CALLS,
} from "../graphql";
import { RANK } from "../runReads";
import { ENDS, junctionEventsOf, type OrderInfo, type RunStep, type TabletCall, type TimedEvent } from "./orderView";

/** What the laptop shows before the server prices the cart. The menu is the server's (OrderMachine.Menu). */
export const MENU_ITEM = { item: "1 × Margherita", price: "$12.00", restaurant: "Pizzeria Napoli" };

// How far apart the two presses of Place order are: a double click, as scripts/recordings/order.mjs sends it.
const DOUBLE_CLICK_MS = 120;
const POLL_MS = 400;

interface Answer {
  id: string;
  revision: number;
  state: string;
  dueAt: string | null;
  context: { items: string[] | null; total: number | null } | null;
  problem: { code: string; message: string } | null;
}

interface Change {
  machineChanged: { revision: number | null; state: string | null; gone: boolean; problem: { code: string; message: string } | null };
}

interface Run {
  id: number;
  trainState: string;
  failureJunction: string | null;
  failureReason: string | null;
}

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));
const money = (total: number) => `$${total.toFixed(2)}`;

/**
 * One food order, placed live: the laptop sends every request of the order machine, and both the laptop and the
 * phone follow the order through machineChanged, each over a socket of its own; neither polls the order. Pressing
 * Place order starts an order (the server mints its id), adds a Margherita, and presses Place order twice in a hurry
 * under one request id, so the server answers the second from the first. The run Ordered invokes is followed through
 * onJunctionEvent, and the tablet's calls are read while SendToRestaurant runs.
 */
export function useFoodOrder() {
  const client = useApolloClient();
  const [events, setEvents] = useState<TimedEvent[]>([]);
  const [steps, setSteps] = useState<RunStep[]>([]);
  const [calls, setCalls] = useState<TabletCall[]>([]);
  const [run, setRun] = useState<Run | null>(null);
  const [base, setBase] = useState(0);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [order, setOrder] = useState<OrderInfo>(MENU_ITEM);
  const [timer, setTimer] = useState<{ due: number; after: number } | null>(null);
  const [scenario, setScenario] = useState<OrderScenarioKey | null>(null);

  // One socket per screen, as on two devices; the run's steps arrive over the page's own socket.
  const sockets = useMemo(() => ({ laptop: persistedSocket(), phone: persistedSocket() }), []);
  useEffect(() => () => void Object.values(sockets).forEach((s) => s.dispose()), [sockets]);

  const generation = useRef(0);
  const stops = useRef<(() => void)[]>([]);
  const orderId = useRef<string | null>(null);
  const laptopState = useRef<string | null>(null);

  const stopFollowing = useCallback(() => {
    stops.current.forEach((stop) => stop());
    stops.current = [];
  }, []);

  /** Clears the page. An order left in the cart is discarded, since a user's open carts count against their drafts. */
  const reset = useCallback(() => {
    generation.current++;
    stopFollowing();
    const left = orderId.current;
    if (left && laptopState.current === "Cart")
      void client.mutate({ mutation: ORDER_DISCARD, variables: { id: left, requestId: crypto.randomUUID() } }).catch(() => undefined);
    orderId.current = null;
    laptopState.current = null;
    setEvents([]);
    setSteps([]);
    setCalls([]);
    setRun(null);
    setTimer(null);
    setProblem(null);
    setBusy(false);
    setScenario(null);
    setOrder(MENU_ITEM);
  }, [client, stopFollowing]);

  useEffect(() => () => {
    generation.current++;
    stopFollowing();
  }, [stopFollowing]);

  const place = useCallback(
    async (key: OrderScenarioKey) => {
      reset();
      const mine = ++generation.current;
      const live = () => generation.current === mine;
      const started = Date.now();
      const at = () => Date.now() - started;
      const add = (...more: TimedEvent[]) => live() && setEvents((all) => [...all, ...more]);
      const chosen = ORDER_SCENARIOS.find((s) => s.key === key) ?? ORDER_SCENARIOS[0];
      setBase(started);
      setBusy(true);
      setScenario(key);

      const answered = new Map<string, number | null>();
      /** One request from the laptop: the request, then the server's whole answer. */
      const send = async (operation: "start" | "addItem" | "placeOrder", label: string, mutation: typeof ORDER_START, variables: Record<string, unknown>) => {
        const requestId = String(variables.requestId);
        add({ t: at(), kind: "request", operation, requestId, label });
        const { data } = await client.mutate({ mutation, variables });
        const answer = data.machines.order[operation] as Answer;
        const revision = answer.problem ? null : answer.revision;
        const replayed = answered.has(requestId) ? !answer.problem && answered.get(requestId) === revision : undefined;
        if (!answered.has(requestId)) answered.set(requestId, revision);
        add({ t: at(), kind: "answer", operation, requestId, revision, state: answer.problem ? null : answer.state, problem: answer.problem, ...(replayed != null ? { replayed } : {}) });
        if (!answer.problem) laptopState.current = answer.state;
        return answer;
      };

      try {
        // What goes wrong with this order, armed before it is placed, as order.mjs arms it.
        await client.mutate({ mutation: ORDER_TROUBLE, variables: { trouble: chosen.trouble } });

        const start = await send("start", "Start an order", ORDER_START, { requestId: crypto.randomUUID() });
        if (start.problem) throw new Error(start.problem.message);
        orderId.current = start.id;

        // Both screens follow the order, each on its own socket.
        const latest = { laptop: 0, phone: 0 };
        for (const screen of ["laptop", "phone"] as const)
          stops.current.push(
            subscribePersisted<Change>(sockets[screen], "OrderChanged", { id: start.id }, ({ machineChanged: change }) => {
              if (!live() || change.problem || change.gone || change.revision == null || change.state == null) return;
              if (change.revision <= latest[screen]) return;
              latest[screen] = change.revision;
              if (screen === "laptop") laptopState.current = change.state;
              add({ t: at(), kind: "push", screen, revision: change.revision, state: change.state });
            }),
          );
        for (let i = 0; i < 50 && live() && (latest.laptop < 1 || latest.phone < 1); i++) await sleep(100);
        if (!live()) return;

        await sleep(500);
        const added = await send("addItem", "Add 1 × Margherita", ORDER_ADD_ITEM, { id: start.id, input: { item: "Margherita" }, requestId: crypto.randomUUID() });
        if (added.problem) throw new Error(added.problem.message);
        const priced = added.context?.total != null ? money(added.context.total) : MENU_ITEM.price;
        const restaurant = (await client.query({ query: TABLET_CALLS })).data.discover.orders.tabletCalls.restaurant as string;
        if (!live()) return;
        setOrder({ item: `${added.context?.items?.length ?? 1} × ${added.context?.items?.[0] ?? "Margherita"}`, price: priced, restaurant });

        if (chosen.key === "left-in-cart") {
          // Nobody presses Place order: the server's timer clears the cart, and both screens hear it by push.
          if (added.dueAt) {
            const due = Date.parse(added.dueAt) - started;
            setTimer({ due, after: due - at() });
          }
          while (live() && laptopState.current !== "Cleared") await sleep(200);
          if (live()) setBusy(false);
          return;
        }

        // Place order, pressed twice in a hurry: one request, sent twice.
        await sleep(700);
        const placedAt = new Date().toISOString();
        const press = crypto.randomUUID();
        const [first] = await Promise.all([
          send("placeOrder", "Place order", ORDER_PLACE, { id: start.id, requestId: press }),
          sleep(DOUBLE_CLICK_MS).then(() => send("placeOrder", "Place order (pressed again)", ORDER_PLACE, { id: start.id, requestId: press })),
        ]);
        if (first.problem) throw new Error(first.problem.message);

        // The run Ordered queued, and its steps.
        let runId: number | null = null;
        for (let i = 0; i < 75 && live() && runId == null; i++) {
          const found = (await client.query({ query: DELIVERY_RUNS, variables: { after: placedAt } })).data.operations.executions.items[0];
          if (found) runId = found.id as number;
          else await sleep(200);
        }
        if (!live() || runId == null) throw new Error(live() ? "The delivery run was not queued." : "");
        const merge = (incoming: RunStep[]) =>
          live() &&
          setSteps((known) => {
            const next = [...known];
            for (const step of incoming) {
              const at = next.findIndex((s) => s.position === step.position);
              if (at < 0) next.push(step);
              else if (RANK[step.state] > RANK[next[at].state]) next[at] = step;
            }
            return next;
          });
        const sub = client.subscribe({ query: ON_JUNCTION_EVENT, variables: { metadataId: runId } }).subscribe({
          next: ({ data }) => data?.onJunctionEvent && merge([data.onJunctionEvent.junction as RunStep]),
        });
        stops.current.push(() => sub.unsubscribe());
        merge((await client.query({ query: JUNCTION_RUNS, variables: { metadataId: runId } })).data.operations.junctionRuns as RunStep[]);

        // Until the run ends: its state, and the tablet's calls since the order was placed.
        const readCalls = async () => {
          const all = (await client.query({ query: TABLET_CALLS })).data.discover.orders.tabletCalls.calls as TabletCall[];
          if (live()) setCalls(all.filter((c) => Date.parse(c.startedAt) >= Date.parse(placedAt)));
        };
        for (;;) {
          if (!live()) return;
          const row = (await client.query({ query: EXECUTION, variables: { id: runId } })).data.operations.execution as Run;
          await readCalls();
          if (!live()) return;
          setRun(row);
          if (row.trainState === "COMPLETED" || row.trainState === "FAILED" || row.trainState === "CANCELLED") break;
          await sleep(POLL_MS);
        }
        merge((await client.query({ query: JUNCTION_RUNS, variables: { metadataId: runId } })).data.operations.junctionRuns as RunStep[]);
        await readCalls();
        // The run's outcome moves the order on; the screens hear it by push.
        for (let i = 0; i < 50 && live() && !ENDS.has(laptopState.current ?? ""); i++) await sleep(100);
        if (live()) setBusy(false);
      } catch (error) {
        if (!live()) return;
        const message = (error as Error).message;
        if (message) setProblem(message);
        setBusy(false);
      }
    },
    [client, reset, sockets],
  );

  const all = useMemo(
    () => [...events, ...junctionEventsOf(base, steps, calls, run && { junction: run.failureJunction, reason: run.failureReason })],
    [events, base, steps, calls, run],
  );

  return { events: all, base, busy, problem, order, timer, scenario, place, reset };
}
