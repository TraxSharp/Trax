// Plays back the food order demo (src/data/order-recordings.json): what the Recovery sample's laptop page and phone
// received from the host while an OrderMachine took an order, with the time each thing arrived. Everything the page
// shows at a moment is worked out from the events that had arrived by then, so this file holds no state of its own.
//
// The recording is read defensively: fields it does not know are ignored, and a missing one leaves its part blank.

export type ScenarioKey = "order" | "tablet-times-out" | "restaurant-closed" | "left-in-cart";

export type TimedEvent = { t: number; gap?: number } & (
  | { kind: "request"; operation: string; requestId: string; label: string }
  | {
      kind: "answer";
      operation: string;
      requestId: string;
      ms: number;
      revision: number | null;
      state: string | null;
      problem: { code: string; message: string } | null;
      replayed?: boolean;
    }
  | { kind: "push"; screen: "laptop" | "phone"; revision: number; state: string }
  | { kind: "junction"; attempt: number; step: string; status: "running" | "completed" | "failed"; message?: string }
  | { kind: "retry"; attempt: number; message: string }
);

export interface Scenario {
  key: ScenarioKey;
  title: string;
  caption: string;
  events: TimedEvent[];
}

export interface OrderRecordings {
  /** The cart timer's duration as the machine declares it (ISO-8601); the recording host shortens the real wait. */
  timerAfter?: string;
  recordedAt: string;
  source: { file: string; code: string };
  lines: Record<string, number>;
  train: { name: string; steps: string[]; source?: { file: string; code: string } };
  scenarios: Scenario[];
}

export type Tone = "running" | "done" | "failed" | "waiting" | "idle";

/** The stages of an order as its customer sees them. */
export type Stage = "cart" | "sent" | "charged" | "cooking" | "courier" | "delivered" | "cancelled" | "cleared";

/**
 * One quiet colour per stage, used wherever that stage appears: its timeline box, the code line lit while it acts,
 * and the phone. Low saturation, none louder than the site's green.
 */
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

/** What the recording does not carry: the order the demo shows. */
export const ORDER = { item: "1 × Margherita", price: "$12.00", restaurant: "Pizzeria Napoli" };

// The phone's clock: the recording starts at 7:42 PM, and the time the recording leaves out still passes on it.
const CLOCK_START = (19 * 60 + 42) * 60 * 1000;

export function clockText(ms: number): string {
  const minutes = Math.floor((CLOCK_START + ms) / 60000) % (24 * 60);
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return `${h % 12 || 12}:${String(m).padStart(2, "0")} ${h < 12 ? "AM" : "PM"}`;
}

/** One notification on the phone, in plain words. `t` is when it arrived, in ms from the start. */
export interface PhoneLine {
  key: string;
  t: number;
  clock: string;
  text: string;
  stage: Stage;
}

/** A stretch of time on the timeline: a state the order was in, or one try of one junction. */
export interface Span {
  key: string;
  label: string;
  from: number;
  to: number | null;
  tone: Tone;
  title?: string;
  /** The run's attempt, for a junction. */
  attempt?: number;
  /** A state the timer moved the order into: when the order was last touched before it. */
  timerFrom?: number;
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
  /** The code line to light (1-based, the file's numbering), how, and the stage acting. */
  lit: { line: number; tone: Tone; stage: Stage } | null;
  /** While the order's run is on its junctions: the junction acting, and how, for the train's own code. */
  trainLit: { step: string; tone: Tone; stage: Stage } | null;
  /** What the laptop says about the last thing it sent, such as the double click. */
  note: string | null;
  progress: { label: string; tone: Tone; stage: Stage }[];
  states: Span[];
  junctions: Span[];
  /** Where the run joins the machine: when it started, and when its outcome moved the order, and to what. */
  link: { start: number; outcome: { t: number; state: string; text: string } | null } | null;
  run: { status: string; tone: Tone } | null;
  finished: boolean;
}

/** What the phone says while each junction runs. A junction not listed shows its own name. */
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

const ENDS = new Set(["Delivered", "Cancelled", "Cleared"]);

/** The scenario's events in time order, with anything malformed dropped. */
export function eventsOf(scenario: Scenario | undefined): TimedEvent[] {
  const events = Array.isArray(scenario?.events) ? scenario.events : [];
  return events.filter((e) => e && typeof e.t === "number" && typeof e.kind === "string").sort((a, b) => a.t - b.t);
}

/** When the scenario's last event arrives. */
export function durationOf(scenario: Scenario | undefined): number {
  const events = eventsOf(scenario);
  return events.length ? events[events.length - 1].t : 0;
}


/** Everything the page shows `now` ms into the scenario. */
export function viewAt(recordings: OrderRecordings, scenario: Scenario | undefined, now: number): View {
  const all = eventsOf(scenario);
  const events = all.filter((e) => e.t <= now);
  const lines = recordings.lines ?? {};
  const order = recordings.train?.steps ?? [];

  let state: string | null = null;
  let revision: number | null = null;
  let phoneState: string | null = null;
  let litKey = null as string | null;
  let note: string | null = null;
  // Assigned inside the loop below, so typed by a cast rather than narrowed to null.
  let lastJunction = null as Extract<TimedEvent, { kind: "junction" }> | null;
  let retrying = false;
  let attempts = 0;
  let failure = null as string | null;
  const phoneLines: PhoneLine[] = [];
  const states: Span[] = [];
  const junctions: Span[] = [];
  const sent = new Map<string, number>();

  let lastT = 0;
  let skipped = 0;
  let added = null as string | null;
  const notify = (key: string, t: number, text: string, stage: Stage) =>
    phoneLines.push({ key, t, clock: clockText(t + skipped), text, stage });
  let outcome = null as { t: number; state: string; text: string } | null;
  const enter = (s: string, t: number, timerFrom?: number) => {
    const open = states[states.length - 1];
    if (open?.label === s) return;
    if (open) open.to = timerFrom ?? t;
    states.push({ key: `${s}-${t}`, label: s, from: t, to: null, tone: "running", ...(timerFrom != null ? { timerFrom } : {}) });
    if (junctions.length && (s === "Delivered" || s === "Cancelled"))
      outcome = { t, state: s, text: s === "Cancelled" ? "failed, so Cancelled" : "its outcome moves the order" };
  };

  events.forEach((e, i) => {
    const key = `${i}`;
    const before = lastT;
    lastT = e.t;
    // The recording host shortens the cart timer; on the phone's clock the wait is the one the machine declares.
    if (e.gap && e.gap > 0)
      skipped += e.kind === "push" && e.state === "Cleared" ? Math.max(e.gap, (durationMs(recordings.timerAfter) ?? 0) - e.t) : e.gap;
    switch (e.kind) {
      case "request": {
        // A request sent again under the same id acts on nothing: the server answers it from the first.
        if (!sent.has(e.requestId)) litKey = `event:${e.operation}`;
        if (e.operation === "addItem") added = e.label;
        sent.set(e.requestId, (sent.get(e.requestId) ?? 0) + 1);
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
                ? was === "Ordered" ? null : "Order placed"
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
            state = e.state;
            enter(e.state, e.t, e.gap ? before : undefined);
            // Only the timer moves a cart nobody touched; only the run moves an order on.
            litKey = e.gap ? "timer" : `state:${e.state}`;
          } else if (e.gap) litKey = "timer";
        }
        break;
      }
      case "junction": {
        lastJunction = e;
        attempts = Math.max(attempts, e.attempt);
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
            ? { SendToRestaurant: "Sent to the restaurant", ChargeCard: `Your card was charged ${ORDER.price}`, PickUp: "A courier picked it up" }[e.step]
            : e.status === "running"
              ? { Cook: "The kitchen is cooking it", Deliver: "On its way to you" }[e.step]
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

  const finished = events.length === all.length && all.length > 0;
  const ended = state != null && ENDS.has(state);
  const last = states[states.length - 1];
  if (last) {
    last.tone = !ended ? "running" : last.label === "Delivered" ? "done" : last.label === "Cancelled" ? "failed" : "waiting";
    states.forEach((s) => s !== last && (s.tone = "done"));
  }

  // The run: one row of junctions, the retried step shown failed then completed.
  const junctionFailed = lastJunction?.status === "failed";
  const run =
    junctions.length === 0
      ? null
      : state === "Delivered"
        ? { status: attempts > 1 ? "Recovered" : "Completed", tone: "done" as Tone }
        : state === "Cancelled"
          ? { status: "Failed", tone: "failed" as Tone }
          : retrying || junctionFailed
            ? { status: retrying ? `Retrying, attempt ${attempts + 1}` : "Failed", tone: "failed" as Tone }
            : { status: attempts > 1 ? `Running, attempt ${attempts}` : "Running", tone: "running" as Tone };

  // The phone's big line: the order's state, or while it is out for delivery, the junction that is running.
  const shown = phoneState ?? null;
  let status = shown ? (STATE_STATUS[shown] ?? shown) : "Nothing yet";
  let detail = "";
  let stage: Stage | null = shown ? stageOfState(shown) : null;
  const j = lastJunction;
  if (shown === "Ordered" && j) {
    if (j.status === "failed" || retrying) {
      status = "Waiting for the restaurant";
      detail = j.message ?? "";
      stage = "sent";
    } else {
      const step = j.status === "completed" ? (order[order.indexOf(j.step) + 1] ?? j.step) : j.step;
      status = STATUS[step] ?? step;
      stage = stageOfJunction(step);
    }
  }
  if (shown === "Ordered" && !j) detail = ORDER.item;
  if (shown === "Cart") detail = `${ORDER.item}, ${ORDER.price}`;
  if (shown === "Delivered") detail = "Enjoy your meal.";
  if (shown === "Cancelled") {
    const charged = junctions.some((x) => x.label === "ChargeCard" && x.tone === "done");
    detail = [failure, charged ? "" : "You were not charged."].filter(Boolean).join(" ");
  }
  if (shown === "Cleared") detail = "Nobody ordered, so it was cleared after a while.";

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
          ? state === "Cleared"
            ? "cleared"
            : "cart"
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
    lit: typeof line === "number" ? { line, tone: litTone, stage: litStage } : null,
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
    run,
    finished,
  };
}

/** One fixed box of the timeline: a state of the order or a junction of its train, filled as far as it got. */
export interface Slot {
  key: string;
  label: string;
  /** How far the box is filled, 0 to 1. */
  fill: number;
  tone: Tone;
  /** Shown when the junction is on a later attempt than the first. */
  attempt?: number;
  /** A box the scenario never reached, once it is over. */
  dimmed: boolean;
  stage: Stage;
  title?: string;
}

/** The timeline: drawn in full from the first frame, its boxes filling as the recording reaches them. */
export interface Track {
  machine: Slot[];
  train: Slot[] | null;
  starts: { lit: boolean } | null;
  outcome: { lit: boolean; text: string; tone: Tone } | null;
  timer: { lit: boolean; text: string } | null;
}

const clamp = (n: number) => (Number.isFinite(n) ? Math.min(1, Math.max(0, n)) : 1);

/** The timeline `now` ms into the scenario; before it starts (a negative `now`), every box is empty. */
export function trackAt(recordings: OrderRecordings, scenario: Scenario | undefined, now: number): Track {
  const duration = durationOf(scenario);
  const final = viewAt(recordings, scenario, duration);
  const finished = scenario != null && now >= duration;
  const train = recordings.train?.name ?? "the train";
  const steps = recordings.train?.steps ?? [];
  const cleared = final.states.find((s) => s.label === "Cleared");
  const names = cleared ? ["Cart", "Cleared"] : ["Cart", "Ordered", "Delivered", "Cancelled"];

  // The latest try of each junction that had started by now.
  const tryOf = (step: string) => [...final.junctions].reverse().find((j) => j.label === step && j.from <= now);
  const completed = steps.filter((step) => {
    const t = tryOf(step);
    return t && t.tone === "done" && t.to != null && t.to <= now;
  }).length;

  // Ordered takes the colour of the junction its train is on.
  const onNow = [...final.junctions].reverse().find((j) => j.from <= now);
  const machine: Slot[] = names.map((name): Omit<Slot, "stage"> => {
    const i = final.states.findIndex((s) => s.label === name);
    const span = final.states[i];
    const next = final.states[i + 1];
    if (!span || now < span.from) return { key: name, label: name, fill: 0, tone: "idle", dimmed: finished };
    if (name === "Cart") {
      const end = next?.from ?? span.from;
      return now < end
        ? { key: name, label: name, fill: clamp((now - span.from) / (end - span.from)), tone: "running", dimmed: false }
        : { key: name, label: name, fill: 1, tone: "done", dimmed: false };
    }
    if (name === "Ordered") {
      const fill = steps.length ? completed / steps.length : 0;
      if (!next || now < next.from)
        return { key: name, label: `Ordered: waiting for ${train}`, fill, tone: "waiting", dimmed: false };
      const failed = next.label === "Cancelled";
      return { key: name, label: name, fill: failed ? fill : 1, tone: failed ? "failed" : "done", dimmed: false };
    }
    return { key: name, label: name, fill: 1, tone: name === "Cancelled" ? "failed" : "done", dimmed: false };
  }).map((slot) => ({ ...slot, stage: slot.key === "Ordered" && onNow ? stageOfJunction(onNow.label) : stageOfState(slot.key) }));

  const trainSlots: Slot[] | null = cleared
    ? null
    : steps.map((step): Slot => {
        const stage = stageOfJunction(step);
        const t = tryOf(step);
        if (!t) return { key: step, label: step, fill: 0, tone: "idle", dimmed: finished, stage };
        // Marked only on a junction tried before: the rest of a retried run is simply the run.
        const tried = final.junctions.filter((j) => j.label === step && j.from <= now).length > 1;
        const attempt = tried && t.attempt && t.attempt > 1 ? t.attempt : undefined;
        if (t.to == null || now < t.to)
          return { key: step, label: step, fill: clamp((now - t.from) / ((t.to ?? now) - t.from)), tone: "running", attempt, dimmed: false, stage };
        return { key: step, label: step, fill: 1, tone: t.tone, attempt, dimmed: false, title: t.title, stage };
      });

  const outcome = final.link?.outcome;
  const gap = scenario?.events.find((e) => e.gap)?.gap ?? 0;
  return {
    machine,
    train: trainSlots,
    starts: final.link ? { lit: now >= final.link.start } : null,
    outcome: cleared
      ? null
      : outcome && now >= outcome.t
        ? { lit: true, text: outcome.text, tone: outcome.state === "Cancelled" ? "failed" : "done" }
        : { lit: false, text: "its outcome moves the order", tone: "idle" },
    timer: cleared
      ? {
          lit: now >= cleared.from,
          text: `after ${durationText(recordings.timerAfter) ?? `${Math.max(1, Math.round((gap + cleared.from - (cleared.timerFrom ?? cleared.from)) / 60000))} min`} (timer)`,
        }
      : null,
  };
}

/** "30 min", "2 h", "45 s" for an ISO-8601 duration such as PT30M; null when there is none or it doesn't parse. */
export function durationText(iso: string | undefined): string | null {
  const m = iso ? /^PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?$/.exec(iso) : null;
  if (!m || (!m[1] && !m[2] && !m[3])) return null;
  const parts = [m[1] && `${m[1]} h`, m[2] && `${m[2]} min`, m[3] && `${m[3]} s`].filter(Boolean);
  return parts.join(" ");
}

/** Milliseconds in an ISO-8601 duration such as PT30M; null when there is none or it doesn't parse. */
export function durationMs(iso: string | undefined): number | null {
  const m = iso ? /^PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?$/.exec(iso) : null;
  if (!m || (!m[1] && !m[2] && !m[3])) return null;
  return ((Number(m[1] ?? 0) * 60 + Number(m[2] ?? 0)) * 60 + Number(m[3] ?? 0)) * 1000;
}
