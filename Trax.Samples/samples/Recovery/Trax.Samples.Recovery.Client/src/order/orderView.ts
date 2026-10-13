// What the food order page shows, worked out from what the laptop and the phone received from the host, each with
// the time it arrived: the laptop's requests and the server's answers, every push each screen heard, and the steps of
// the run the order's Ordered state invoked. The page keeps only that list of events; everything on screen is a pure
// function of it, so it is tested alone.

export type TimedEvent = { t: number } & (
  | { kind: "request"; operation: string; requestId: string; label: string }
  | {
      kind: "answer";
      operation: string;
      requestId: string;
      revision: number | null;
      state: string | null;
      problem: { code: string; message: string } | null;
      /** A request sent again under the same id, which the server answered from the first. */
      replayed?: boolean;
    }
  | { kind: "push"; screen: "laptop" | "phone"; revision: number; state: string }
  | { kind: "junction"; attempt: number; step: string; status: "running" | "completed" | "failed"; message?: string }
  | { kind: "retry"; attempt: number; message: string }
);

export type Tone = "running" | "done" | "failed" | "waiting" | "idle";

/** The stages of an order as its customer sees them. */
export type Stage = "cart" | "sent" | "charged" | "cooking" | "courier" | "delivered" | "cancelled" | "cleared";

/** One quiet colour per stage, wherever that stage appears: its timeline box, the lit code line, the phone. */
export const STAGES: Record<Stage, { label: string; color: string }> = {
  cart: { label: "In your cart", color: "#8b95a7" },
  sent: { label: "Sent to the restaurant", color: "#c4a468" },
  charged: { label: "Charged", color: "#a093c6" },
  cooking: { label: "Cooking", color: "#c68f68" },
  courier: { label: "With the courier", color: "#78a6c4" },
  delivered: { label: "Delivered", color: "#6fa86f" },
  cancelled: { label: "Cancelled", color: "#c47a85" },
  cleared: { label: "Cleared", color: "#8d8a84" },
};

/** A stage's colour at a strength: a quiet tint for fills, a stronger one for borders. */
export const tint = (stage: Stage, alpha: number) =>
  `${STAGES[stage].color}${Math.round(alpha * 255)
    .toString(16)
    .padStart(2, "0")}`;

/** DeliverOrderTrain's junctions, in the order its chain declares them. */
export const TRAIN_STEPS = ["SendToRestaurant", "ChargeCard", "Cook", "PickUp", "Deliver"];

const JUNCTION_STAGE: Record<string, Stage> = {
  SendToRestaurant: "sent",
  ChargeCard: "charged",
  Cook: "cooking",
  PickUp: "courier",
  Deliver: "courier",
};

const STATE_STAGE: Record<string, Stage> = {
  Cart: "cart",
  Ordered: "sent",
  Delivered: "delivered",
  Cancelled: "cancelled",
  Cleared: "cleared",
};

export const stageOfJunction = (step: string): Stage => JUNCTION_STAGE[step] ?? "sent";
export const stageOfState = (state: string): Stage => STATE_STAGE[state] ?? "cart";

/** What the order is: the cart as the server priced it, and the restaurant it goes to. */
export interface OrderInfo {
  item: string;
  price: string;
  restaurant: string;
}

/** The phone's clock: the time of day, `ms` after the order began at `base` (epoch milliseconds). */
export function clockText(base: number, ms: number): string {
  return new Date(base + ms).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" });
}

/** One notification on the phone, in plain words. `t` is when it arrived, in ms from the start. */
export interface PhoneLine {
  key: string;
  t: number;
  clock: string;
  text: string;
  stage: Stage;
}

/** A stretch of time: a state the order was in, or one try of one junction. */
export interface Span {
  key: string;
  label: string;
  from: number;
  to: number | null;
  tone: Tone;
  title?: string;
  attempt?: number;
}

export interface View {
  /** The machine's state as the laptop last heard it. */
  state: string | null;
  revision: number | null;
  phone: {
    status: string;
    detail: string;
    stage: Stage | null;
    lines: PhoneLine[];
    /** The machine's states as dots: the ones it passed, the one it is in, the ones still ahead. */
    machine: { label: string; stage: Stage; at: "done" | "current" | "ahead" }[];
  };
  /** The machine's code line to light (1-based, the file's numbering), and the stage acting. */
  lit: { line: number; tone: Tone; stage: Stage } | null;
  /** While the order's run is on its junctions: the junction acting, and how, for the train's own code. */
  trainLit: { step: string; tone: Tone; stage: Stage } | null;
  /** What the laptop says about the last thing it sent, such as a refusal. */
  note: string | null;
  progress: { label: string; tone: Tone; stage: Stage }[];
  states: Span[];
  junctions: Span[];
  /** When the run started, and when its outcome moved the order, and to what. */
  link: { start: number; outcome: { t: number; state: string; text: string } | null } | null;
}

/** What the phone says while each junction runs. */
const STATUS: Record<string, string> = {
  SendToRestaurant: "Sending to the restaurant",
  ChargeCard: "Charging your card",
  Cook: "Cooking",
  PickUp: "With the courier",
  Deliver: "With the courier",
};

const STATE_STATUS: Record<string, string> = {
  Cart: "In your cart",
  Ordered: "Order placed",
  Delivered: "Delivered",
  Cancelled: "Cancelled",
  Cleared: "Cart cleared",
};

export const ENDS = new Set(["Delivered", "Cancelled", "Cleared"]);

/** Everything the page shows once `events` have arrived. `base` is when the order began, in epoch milliseconds. */
export function viewOf(events: TimedEvent[], lines: Record<string, number>, order: OrderInfo, base: number): View {
  const sorted = [...events].sort((a, b) => a.t - b.t);
  let state: string | null = null;
  let revision: number | null = null;
  let phoneState: string | null = null;
  let litKey = null as string | null;
  let note: string | null = null;
  let lastJunction = null as Extract<TimedEvent, { kind: "junction" }> | null;
  let retrying = false;
  let failure = null as string | null;
  const phoneLines: PhoneLine[] = [];
  const states: Span[] = [];
  const junctions: Span[] = [];
  const sent = new Set<string>();
  let added = null as string | null;
  let outcome = null as { t: number; state: string; text: string } | null;

  const notify = (key: string, t: number, text: string, stage: Stage) => phoneLines.push({ key, t, clock: clockText(base, t), text, stage });
  const enter = (s: string, t: number) => {
    const open = states[states.length - 1];
    if (open?.label === s) return;
    if (open) open.to = t;
    states.push({ key: `${s}-${t}`, label: s, from: t, to: null, tone: "running" });
    if (junctions.length && (s === "Delivered" || s === "Cancelled"))
      outcome = { t, state: s, text: s === "Cancelled" ? "failed, so Cancelled" : "its outcome moves the order" };
  };

  sorted.forEach((e, i) => {
    const key = `${i}`;
    switch (e.kind) {
      case "request": {
        // A request sent again under the same id acts on nothing: the server answers it from the first.
        if (!sent.has(e.requestId)) litKey = `event:${e.operation}`;
        if (e.operation === "addItem") added = e.label;
        sent.add(e.requestId);
        break;
      }
      case "answer": {
        // A second Place order is answered as a replay, or refused once the order is placed: nothing to say.
        if (e.replayed || (e.problem && e.operation === "placeOrder" && state === "Ordered")) break;
        if (e.problem) {
          note = e.problem.message;
          break;
        }
        if (e.revision != null && (revision == null || e.revision >= revision)) revision = e.revision;
        if (e.state) {
          state = e.state;
          enter(e.state, e.t);
          litKey = `state:${e.state}`;
        }
        break;
      }
      case "push": {
        if (e.screen === "phone") {
          const was = phoneState;
          phoneState = e.state;
          const charged = junctions.some((x) => x.label === "ChargeCard" && x.tone === "done");
          const text =
            e.state === "Cart"
              ? was && added
                ? `${added.replace(/^Add\b/, "Added")} to your cart`
                : null
              : e.state === "Ordered"
                ? was === "Ordered"
                  ? null
                  : "Order placed"
                : e.state === "Delivered"
                  ? "Delivered. Enjoy your meal."
                  : e.state === "Cancelled"
                    ? `Cancelled. ${failure ?? ""} ${charged ? "" : "You were not charged."}`.replace(/\s+/g, " ").trim()
                    : e.state === "Cleared"
                      ? "Your cart was cleared"
                      : e.state;
          if (text) notify(key, e.t, text, stageOfState(e.state));
        }
        if (revision == null || e.revision >= revision) {
          revision = e.revision;
          if (state !== e.state) {
            const timer = state === "Cart" && e.state === "Cleared";
            state = e.state;
            enter(e.state, e.t);
            // Only the timer moves a cart nobody touched; only the run moves an order on.
            litKey = timer ? "timer" : `state:${e.state}`;
          }
        }
        break;
      }
      case "junction": {
        lastJunction = e;
        litKey = "invoke";
        if (e.status === "running") {
          retrying = false;
          junctions.push({ key: `${e.attempt}-${e.step}-${e.t}`, label: e.step, from: e.t, to: null, tone: "running", attempt: e.attempt });
        } else {
          const open = [...junctions].reverse().find((j) => j.label === e.step && j.to == null);
          const span = open ?? { key: `${e.attempt}-${e.step}-${e.t}`, label: e.step, from: e.t, to: null, tone: "running" as Tone, attempt: e.attempt };
          if (!open) junctions.push(span);
          span.to = e.t;
          span.tone = e.status === "failed" ? "failed" : "done";
          span.title = [`${e.step}, attempt ${e.attempt}`, e.message].filter(Boolean).join("\n");
          if (e.status === "failed") failure = e.message ?? `${e.step} failed`;
        }
        const told =
          e.status === "completed"
            ? ({ SendToRestaurant: "Sent to the restaurant", ChargeCard: `Your card was charged ${order.price}`, PickUp: "A courier picked it up" } as Record<string, string>)[e.step]
            : e.status === "running"
              ? ({ Cook: "The kitchen is cooking it", Deliver: "On its way to you" } as Record<string, string>)[e.step]
              : undefined;
        if (told) notify(key, e.t, told, stageOfJunction(e.step));
        break;
      }
      case "retry": {
        retrying = true;
        litKey = "invoke";
        notify(key, e.t, "The restaurant has not answered yet; trying again", "sent");
        break;
      }
    }
  });

  const ended = state != null && ENDS.has(state);
  const last = states[states.length - 1];
  if (last) {
    last.tone = !ended ? "running" : last.label === "Delivered" ? "done" : last.label === "Cancelled" ? "failed" : "waiting";
    states.forEach((s) => s !== last && (s.tone = "done"));
  }

  // The phone's big line: the order's state, or while it is out for delivery, the junction that is running.
  const shown = phoneState;
  let status = shown ? (STATE_STATUS[shown] ?? shown) : "Nothing yet";
  let detail = "";
  let stage: Stage | null = shown ? stageOfState(shown) : null;
  const j = lastJunction;
  const junctionFailed = j?.status === "failed";
  if (shown === "Ordered" && j) {
    if (junctionFailed || retrying) {
      status = "Waiting for the restaurant";
      detail = j.message ?? "";
      stage = "sent";
    } else {
      const step = j.status === "completed" ? (TRAIN_STEPS[TRAIN_STEPS.indexOf(j.step) + 1] ?? j.step) : j.step;
      status = STATUS[step] ?? step;
      stage = stageOfJunction(step);
    }
  }
  if (shown === "Ordered" && !j) detail = order.item;
  if (shown === "Cart") detail = `${order.item}, ${order.price}`;
  if (shown === "Delivered") detail = "Enjoy your meal.";
  if (shown === "Cancelled") {
    const charged = junctions.some((x) => x.label === "ChargeCard" && x.tone === "done");
    detail = [failure, charged ? "" : "You were not charged."].filter(Boolean).join(" ");
  }
  if (shown === "Cleared") detail = "Nobody ordered, so the server cleared it.";

  // The machine as dots: Cart, Ordered, and how it ended (Delivered until it ends otherwise).
  const path = shown === "Cleared" ? ["Cart", "Cleared"] : ["Cart", "Ordered", shown === "Cancelled" ? "Cancelled" : "Delivered"];
  const at = shown ? path.indexOf(shown) : -1;
  const machine = path.map((label, i) => ({
    label,
    stage: stageOfState(label),
    at: (i < at ? "done" : i === at ? "current" : "ahead") as "done" | "current" | "ahead",
  }));

  // The lit line follows the last thing that acted; while the train runs, that is the line that invokes it.
  const line = litKey ? lines[litKey] : undefined;
  const litTone: Tone =
    state === "Cancelled" || (litKey === "invoke" && (junctionFailed || retrying))
      ? "failed"
      : litKey === "invoke" || litKey?.startsWith("event:")
        ? "running"
        : "done";
  const litStage: Stage =
    litTone === "failed"
      ? "cancelled"
      : litKey === "invoke"
        ? j
          ? stageOfJunction(j.step)
          : "sent"
        : litKey === "timer"
          ? "cleared"
          : litKey?.startsWith("state:")
            ? stageOfState(litKey.slice(6))
            : "cart";

  const cooked = junctions.some((x) => x.label === "Cook");
  const progress: { label: string; tone: Tone; stage: Stage }[] = [
    { label: "Ordered", tone: state === "Ordered" || ended ? "done" : "idle", stage: "sent" },
    { label: "Cooking", tone: state === "Delivered" ? "done" : cooked ? "running" : "idle", stage: "cooking" },
    { label: "Delivered", tone: state === "Delivered" ? "done" : "idle", stage: "delivered" },
  ];
  if (state === "Cleared") progress[0].tone = "idle";
  // A cancelled order never cooked: the chips it did not reach stay as they were, and the phone says why.
  if (state === "Cancelled") progress.forEach((p) => p.label !== "Ordered" && (p.tone = "idle"));
  if (state === "Ordered" && cooked) progress[1].tone = junctions.some((x) => x.label === "Cook" && x.tone === "done") ? "done" : "running";

  return {
    state,
    revision,
    phone: { status, detail, stage, lines: phoneLines, machine },
    lit: typeof line === "number" && line > 0 ? { line, tone: litTone, stage: litStage } : null,
    trainLit:
      litKey === "invoke" && j
        ? {
            step: j.step,
            tone: j.status === "failed" ? "failed" : j.status === "completed" ? "done" : "running",
            stage: j.status === "failed" ? "cancelled" : stageOfJunction(j.step),
          }
        : null,
    note,
    progress,
    states,
    junctions,
    link: junctions.length ? { start: junctions[0].from, outcome } : null,
  };
}

/** One fixed box of the timeline: a state of the order or a junction of its train, filled as far as it got. */
export interface Slot {
  key: string;
  label: string;
  fill: number;
  tone: Tone;
  /** Shown on a junction tried more than once. */
  attempt?: number;
  /** A box the order never reached, once it is over. */
  dimmed: boolean;
  stage: Stage;
  title?: string;
}

/** The timeline: drawn in full before anything happens, its boxes filling as the events arrive. */
export interface Track {
  machine: Slot[];
  train: Slot[] | null;
  starts: { lit: boolean } | null;
  outcome: { lit: boolean; text: string; tone: Tone } | null;
  timer: { lit: boolean; text: string } | null;
}

const clamp = (n: number) => (Number.isFinite(n) ? Math.min(1, Math.max(0, n)) : 1);

/** The fill of a box still running with no known end: the time it has run, on a curve that never quite fills. */
const growing = (ms: number) => Math.min(0.92, 1 - Math.exp(-Math.max(0, ms) / 900));

/**
 * The timeline `now` ms after the order began. `cart` lays it out for a cart left alone (Cart, the timer, Cleared);
 * otherwise Cart, Ordered over the train's junctions, then Delivered and Cancelled. `timer` is the cart's timer as the
 * server reported it: when it is due, and how long it waits, both from the order's start. `over` dims the boxes the
 * order never reached.
 */
export function trackOf(
  view: View,
  now: number,
  options: { cart: boolean; timer: { due: number; after: number } | null; over: boolean },
): Track {
  const names = options.cart ? ["Cart", "Cleared"] : ["Cart", "Ordered", "Delivered", "Cancelled"];
  const done = (step: string) => view.junctions.some((j) => j.label === step && j.tone === "done");
  const completed = TRAIN_STEPS.filter(done).length;
  const onNow = view.junctions[view.junctions.length - 1];

  const machine: Slot[] = names
    .map((name): Omit<Slot, "stage"> => {
      const i = view.states.findIndex((s) => s.label === name);
      const span = view.states[i];
      const next = view.states[i + 1];
      if (!span) return { key: name, label: name, fill: 0, tone: "idle", dimmed: options.over };
      if (name === "Cart") {
        if (next) return { key: name, label: name, fill: 1, tone: "done", dimmed: false };
        // Still in the cart: the timer, when there is one, fills it.
        const fill = options.timer ? clamp((now - span.from) / (options.timer.due - span.from)) : growing(now - span.from);
        return { key: name, label: name, fill, tone: "running", dimmed: false };
      }
      if (name === "Ordered") {
        const fill = completed / TRAIN_STEPS.length;
        if (!next) return { key: name, label: "Ordered: waiting for DeliverOrderTrain", fill, tone: "waiting", dimmed: false };
        const failed = next.label === "Cancelled";
        return { key: name, label: name, fill: failed ? fill : 1, tone: failed ? "failed" : "done", dimmed: false };
      }
      return { key: name, label: name, fill: 1, tone: name === "Cancelled" ? "failed" : "done", dimmed: false };
    })
    .map((slot) => ({ ...slot, stage: slot.key === "Ordered" && onNow ? stageOfJunction(onNow.label) : stageOfState(slot.key) }));

  const train: Slot[] | null = options.cart
    ? null
    : TRAIN_STEPS.map((step): Slot => {
        const stage = stageOfJunction(step);
        const tries = view.junctions.filter((j) => j.label === step);
        const t = tries[tries.length - 1];
        if (!t) return { key: step, label: step, fill: 0, tone: "idle", dimmed: options.over, stage };
        // Marked only on a junction tried more than once.
        const attempt = tries.length > 1 && t.attempt && t.attempt > 1 ? t.attempt : undefined;
        if (t.to == null) return { key: step, label: step, fill: growing(now - t.from), tone: "running", attempt, dimmed: false, stage };
        return { key: step, label: step, fill: 1, tone: t.tone, attempt, dimmed: false, title: t.title, stage };
      });

  const cleared = view.states.find((s) => s.label === "Cleared");
  const outcome = view.link?.outcome;
  return {
    machine,
    train,
    starts: options.cart ? null : { lit: view.link != null },
    outcome: options.cart
      ? null
      : outcome
        ? { lit: true, text: outcome.text, tone: outcome.state === "Cancelled" ? "failed" : "done" }
        : { lit: false, text: "its outcome moves the order", tone: "idle" },
    timer: options.cart
      ? { lit: cleared != null, text: options.timer ? `after ${durationText(options.timer.after)} (timer)` : "the cart's timer" }
      : null,
  };
}

/** A duration in words: "15 s", "30 min", "1 h 30 min". */
export function durationText(ms: number): string {
  const seconds = Math.round(ms / 1000);
  if (seconds < 60) return `${seconds} s`;
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes} min`;
  return [`${Math.floor(minutes / 60)} h`, minutes % 60 ? `${minutes % 60} min` : ""].filter(Boolean).join(" ");
}

/** One step of the order's run, as operations.junctionRuns and onJunctionEvent report it. */
export interface RunStep {
  position: number;
  name: string;
  state: "IN_PROGRESS" | "COMPLETED" | "FAILED" | "CANCELLED";
  startedAt: string;
  endedAt: string | null;
}

/** One call to the restaurant's tablet, as discover { orders { tabletCalls } } lists it. */
export interface TabletCall {
  attempt: number;
  startedAt: string;
  endedAt: string | null;
  problem: string | null;
}

/**
 * The run's steps as junction events, timed by each step's own start and end (ms after `base`, epoch milliseconds).
 * SendToRestaurant's tries are the tablet's calls: a call that went unanswered and was followed by another is the
 * step failing, a retry, and the step running again on the next attempt. `failure` is the run's failure reason, for a
 * step that failed it.
 */
export function junctionEventsOf(base: number, steps: RunStep[], calls: TabletCall[], failure: { junction: string | null; reason: string | null } | null): TimedEvent[] {
  const at = (iso: string) => Date.parse(iso) - base;
  const events: TimedEvent[] = [];
  for (const s of [...steps].sort((a, b) => a.position - b.position)) {
    events.push({ t: at(s.startedAt), kind: "junction", attempt: 1, step: s.name, status: "running" });
    const tries = s.name === "SendToRestaurant" ? [...calls].sort((a, b) => a.attempt - b.attempt) : [];
    for (const call of tries) {
      const next = tries.find((c) => c.attempt === call.attempt + 1);
      if (!call.problem || !call.endedAt || !next) continue;
      events.push({ t: at(call.endedAt), kind: "junction", attempt: call.attempt, step: s.name, status: "failed", message: call.problem });
      const backoff = Math.round((Date.parse(next.startedAt) - Date.parse(call.endedAt)) / 100) / 10;
      events.push({ t: at(call.endedAt), kind: "retry", attempt: next.attempt, message: `Trying the tablet again in ${backoff} s (attempt ${next.attempt}).` });
      events.push({ t: at(next.startedAt), kind: "junction", attempt: next.attempt, step: s.name, status: "running" });
    }
    if (s.state === "IN_PROGRESS" || !s.endedAt) continue;
    const attempt = tries.length ? Math.max(...tries.map((c) => c.attempt)) : 1;
    const failed = s.state !== "COMPLETED";
    const message = failed ? ((failure?.junction === s.name ? failure.reason : null) ?? tries[tries.length - 1]?.problem ?? "The step failed.") : undefined;
    events.push({ t: at(s.endedAt), kind: "junction", attempt, step: s.name, status: failed ? "failed" : "completed", ...(message ? { message } : {}) });
  }
  return events;
}
