import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { createClient } from "graphql-ws";
import { createMachineServer, persistedMessageReplacer } from "@traxsharp/state-machine/client";
import { ORDER_SCENARIOS, type OrderScenarioKey } from "../cases";
import { connection, SOCKET_URL } from "../connection";
import { followRunProgress, order, orderServer, ordersTabletCalls, orderTrouble, readRunProgress } from "../trax/index.g";
import { orderDocuments } from "../trax/order/order.client.g";
import { sendDocument } from "../trax/client.g";
import { RANK } from "../runReads";
import { ENDS, junctionEventsOf, type OrderInfo, type RunStep, type TabletCall, type TimedEvent } from "./orderView";

/** What the laptop shows before the server prices the cart. The menu is the server's (OrderMachine.Menu). */
export const MENU_ITEM = { item: "1 × Margherita", price: "$12.00", restaurant: "Pizzeria Napoli" };

// How far apart the two presses of Place order are: a double click, as scripts/recordings/order.mjs sends it.
const DOUBLE_CLICK_MS = 120;
const POLL_MS = 400;

interface Run {
  trainState: string;
  failureJunction: string | null;
  failureReason: string | null;
}

/** A document of the order machine, by operation, from the generated client. */
const documentOf = (operation: string) => {
  const found = orderDocuments.find((d) => d.operation === operation);
  if (!found) throw new Error(`The generated client has no ${operation} document for the order.`);
  return found;
};

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));
const money = (total: number) => `$${total.toFixed(2)}`;

/**
 * One food order, placed live: the laptop sends every request of the order machine through the generated client, and
 * both the laptop and the phone follow the order through its push, orderChanged, each over a socket of its own; neither
 * polls the order. Pressing Place order starts an order (the server mints its id), adds a Margherita, and presses Place
 * order twice in a hurry under one request id, so the server answers the second from the first. The run Ordered invokes
 * is the one the order's answer names (invokedRun), followed through runProgress, and the tablet's calls are read while
 * SendToRestaurant runs.
 */
export function useFoodOrder() {
  const [events, setEvents] = useState<TimedEvent[]>([]);
  const [steps, setSteps] = useState<RunStep[]>([]);
  const [calls, setCalls] = useState<TabletCall[]>([]);
  const [run, setRun] = useState<Run | null>(null);
  const [base, setBase] = useState(0);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [shown, setShown] = useState<OrderInfo>(MENU_ITEM);
  const [timer, setTimer] = useState<{ due: number; after: number } | null>(null);
  const [scenario, setScenario] = useState<OrderScenarioKey | null>(null);

  // The phone is another device: a socket and a server of its own. The laptop is the generated client's.
  const phone = useMemo(() => {
    const socket = createClient({
      url: SOCKET_URL,
      connectionParams: connection().connectionParams,
      lazy: true,
      jsonMessageReplacer: persistedMessageReplacer,
    });
    return { socket, server: createMachineServer(connection().url, { headers: connection().headers, machine: order, documents: orderDocuments, socket }) };
  }, []);
  useEffect(() => () => void phone.socket.dispose(), [phone]);

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
      void sendDocument(documentOf("discard").id, { id: left, requestId: crypto.randomUUID() }, ["machines", "order", "discard"]).catch(() => undefined);
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
    setShown(MENU_ITEM);
  }, [stopFollowing]);

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
      const laptop = orderServer();
      setBase(started);
      setBusy(true);
      setScenario(key);

      const answered = new Map<string, number | null>();
      type Answered = { problem: { code: string; message: string } | null; id: string | null; revision: number | null; state: string | null; context: Record<string, unknown>; dueAt: string | null };
      /** One request from the laptop: the request, then the server's whole answer. */
      const send = async (operation: "start" | "addItem" | "placeOrder", label: string, requestId: string, request: () => Promise<Answered>) => {
        add({ t: at(), kind: "request", operation, requestId, label });
        const answer = await request();
        const revision = answer.problem ? null : answer.revision;
        const replayed = answered.has(requestId) ? !answer.problem && answered.get(requestId) === revision : undefined;
        if (!answered.has(requestId)) answered.set(requestId, revision);
        add({ t: at(), kind: "answer", operation, requestId, revision, state: answer.problem ? null : answer.state, problem: answer.problem, ...(replayed != null ? { replayed } : {}) });
        if (!answer.problem) laptopState.current = answer.state;
        return answer;
      };
      const answerOf = (response: Awaited<ReturnType<typeof laptop.event>> | Awaited<ReturnType<typeof laptop.start>>): Answered =>
        response.kind === "problem"
          ? { problem: { code: response.code, message: response.message }, id: null, revision: null, state: null, context: {}, dueAt: null }
          : {
              problem: null,
              id: "id" in response ? response.id : orderId.current,
              revision: response.snapshot.revision,
              state: response.snapshot.state,
              context: response.snapshot.context,
              dueAt: response.snapshot.dueAt ?? null,
            };
      const event = (trigger: string, requestId: string, input?: unknown) => async () =>
        answerOf(await laptop.event({ id: orderId.current!, trigger, requestId, ...(input === undefined ? {} : { input }) }));

      try {
        // What goes wrong with this order, armed before it is placed, as order.mjs arms it.
        await orderTrouble({ trouble: chosen.trouble } as Parameters<typeof orderTrouble>[0]);

        const startId = crypto.randomUUID();
        const start = await send("start", "Start an order", startId, async () => answerOf(await laptop.start(undefined, startId)));
        if (start.problem || !start.id) throw new Error(start.problem?.message ?? "The order did not start.");
        orderId.current = start.id;

        // Both screens follow the order, each on its own socket.
        const latest = { laptop: 0, phone: 0 };
        for (const [screen, server] of [["laptop", laptop], ["phone", phone.server]] as const)
          stops.current.push(
            server.subscribe(start.id, (message) => {
              if (!live() || message.type !== "snapshot") return;
              const change = message.snapshot;
              if (change.revision <= latest[screen]) return;
              latest[screen] = change.revision;
              if (screen === "laptop") laptopState.current = change.state;
              add({ t: at(), kind: "push", screen, revision: change.revision, state: change.state });
            }),
          );
        for (let i = 0; i < 50 && live() && (latest.laptop < 1 || latest.phone < 1); i++) await sleep(100);
        if (!live()) return;

        await sleep(500);
        const addId = crypto.randomUUID();
        const added = await send("addItem", "Add 1 × Margherita", addId, event("AddItem", addId, { item: "Margherita" }));
        if (added.problem) throw new Error(added.problem.message);
        const items = added.context.items as string[] | null | undefined;
        const total = added.context.total as number | null | undefined;
        const priced = total != null ? money(total) : MENU_ITEM.price;
        const restaurant = (await ordersTabletCalls()).restaurant;
        if (!live()) return;
        setShown({ item: `${items?.length ?? 1} × ${items?.[0] ?? "Margherita"}`, price: priced, restaurant });

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
          send("placeOrder", "Place order", press, event("PlaceOrder", press)),
          sleep(DOUBLE_CLICK_MS).then(() => send("placeOrder", "Place order (pressed again)", press, event("PlaceOrder", press))),
        ]);
        if (first.problem) throw new Error(first.problem.message);

        // The run Ordered queued: the order's answer names it while the order waits on it, for its owner only.
        const read = await sendDocument<{ invokedRun: { runId: string } | null } | null>(documentOf("get").id, { id: start.id }, ["machines", "order", "get"]);
        const runId = read?.invokedRun?.runId;
        if (!live()) return;
        if (!runId) throw new Error("The delivery run was not queued.");
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

        // Until the run ends: its steps and state, and the tablet's calls since the order was placed.
        const readCalls = async () => {
          const all = (await ordersTabletCalls()).calls as TabletCall[];
          if (live()) setCalls(all.filter((c) => Date.parse(c.startedAt) >= Date.parse(placedAt)));
        };
        let ended = false;
        const follow = (state: string | null | undefined, failure: string | null | undefined) => {
          if (!state || !live()) return;
          setRun((r) => {
            const failed = state === "FAILED";
            return { trainState: state, failureJunction: failed ? (r?.failureJunction ?? null) : null, failureReason: failure ?? r?.failureReason ?? null };
          });
          if (state === "COMPLETED" || state === "FAILED" || state === "CANCELLED") ended = true;
        };
        stops.current.push(
          followRunProgress(runId, (e) => {
            if (e.step) merge([e.step as unknown as RunStep]);
            follow(e.state, e.failure);
          }),
        );
        const first_ = await readRunProgress(runId);
        merge(first_.steps as unknown as RunStep[]);
        follow(first_.state, first_.failure);
        while (live() && !ended) {
          await readCalls();
          await sleep(POLL_MS);
        }
        if (!live()) return;
        merge((await readRunProgress(runId)).steps as unknown as RunStep[]);
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
    [phone, reset],
  );

  const all = useMemo(() => {
    const failedStep = steps.find((s) => s.state === "FAILED")?.name ?? null;
    return [...events, ...junctionEventsOf(base, steps, calls, run && { junction: failedStep, reason: run.failureReason })];
  }, [events, base, steps, calls, run]);

  return { events: all, base, busy, problem, order: shown, timer, scenario, place, reset };
}
