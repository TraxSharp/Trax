import { useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { ORDER_SCENARIOS, type OrderScenarioKey } from "../cases";
import { ENDS, STAGES, tint, trackOf, viewOf, type Slot, type Stage, type Tone, type Track, type View } from "../order/orderView";
import { MENU_ITEM, useFoodOrder } from "../order/useFoodOrder";
import { ORDER_SOURCES, orderLines } from "../sources";
import { CodePanel } from "./CodePanel";
import { Group } from "./Group";

const LINES = orderLines(ORDER_SOURCES.machine.code);

const IDLE_PROGRESS: { label: string; tone: Tone; stage: Stage }[] = [
  { label: "Ordered", tone: "idle", stage: "sent" },
  { label: "Cooking", tone: "idle", stage: "cooking" },
  { label: "Delivered", tone: "idle", stage: "delivered" },
];

/**
 * The food order page: a laptop that places an order with the order machine, the machine's code (and the train's
 * while the order's run is on its junctions) with the acting line lit, a phone that follows the order on a socket of
 * its own, and the timeline of the machine and the run it invoked.
 */
export function FoodOrder() {
  const [key, setKey] = useState<OrderScenarioKey>(ORDER_SCENARIOS[0].key);
  const food = useFoodOrder();
  const started = food.scenario != null;

  const view = useMemo(() => (started ? viewOf(food.events, LINES, food.order, food.base) : null), [started, food.events, food.order, food.base]);
  const over = view?.state != null && ENDS.has(view.state) && !food.busy;

  // A clock for the boxes that fill as they run.
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!food.busy) return;
    const timer = setInterval(() => setNow(Date.now()), 80);
    return () => clearInterval(timer);
  }, [food.busy]);

  const layout = food.scenario ?? key;
  const track = useMemo(
    () => trackOf(view ?? viewOf([], LINES, MENU_ITEM, 0), started ? now - food.base : 0, { cart: layout === "left-in-cart", timer: food.timer, over }),
    [view, started, now, food.base, layout, food.timer, over],
  );

  // Counts orders, so the code panel starts each one following it again.
  const [orders, setOrders] = useState(0);
  const press = () => {
    setOrders((n) => n + 1);
    void food.place(key);
  };

  const leaves = key === "left-in-cart";

  return (
    <div className="demo">
      <div className="controls">
        <Group label="Scenario">
          <select
            className="select"
            aria-label="Scenario"
            value={key}
            disabled={food.busy}
            onChange={(e) => {
              setKey(e.target.value as OrderScenarioKey);
              food.reset();
            }}
          >
            {ORDER_SCENARIOS.map((s) => (
              <option key={s.key} value={s.key}>
                {s.title}
              </option>
            ))}
          </select>
        </Group>

        <Group label="Laptop">
          <div className="cart-line">
            <span>{food.order.item}</span>
            <span className="muted">{food.order.price}</span>
          </div>
          <button className="primary" onClick={press} disabled={food.busy}>
            {leaves ? "Leave it in the cart" : "Place order"}
          </button>
          <button className="action" onClick={food.reset} disabled={!started}>
            Reset
          </button>
          <p className="signal-line">{food.problem ? <span className="run-problem">{food.problem}</span> : view?.note}</p>
        </Group>

        <Group label="Progress">
          <ol className="progress">
            {(view?.progress ?? IDLE_PROGRESS).map((p) => (
              <li
                key={p.label}
                style={
                  p.tone === "idle"
                    ? undefined
                    : { borderColor: tint(p.stage, 0.6), color: STAGES[p.stage].color, background: p.tone === "running" ? tint(p.stage, 0.12) : undefined }
                }
              >
                {p.label}
              </li>
            ))}
          </ol>
        </Group>
      </div>

      <OrderCode key={orders} view={view} />
      <PhonePanel view={view} restaurant={food.order.restaurant} />
      <OrderTimeline track={track} />
    </div>
  );
}

/**
 * The machine's code, or the train's while the order's run is on its junctions: the panel follows the run, and a file
 * tab picked by hand holds until the next order.
 */
function OrderCode({ view }: { view: View | null }) {
  const [picked, setPicked] = useState<string | null>(null);
  const shown = picked ?? (view?.trainLit ? "train" : "machine");
  const trainLine = view?.trainLit ? ORDER_SOURCES.train.code.split("\n").findIndex((l) => l.includes(`Chain<${view.trainLit!.step}>`)) : -1;
  const lit =
    shown === "train"
      ? view?.trainLit && trainLine >= 0
        ? { line: trainLine, color: STAGES[view.trainLit.stage].color }
        : null
      : view?.lit
        ? { line: view.lit.line - 1, color: STAGES[view.lit.stage].color }
        : null;
  return (
    <CodePanel
      tabs={[
        { key: "machine", source: ORDER_SOURCES.machine, part: "machine" },
        { key: "train", source: ORDER_SOURCES.train, part: "chain" },
      ]}
      shown={shown}
      onPick={setPicked}
      lit={lit}
      aside={view?.revision != null ? <span title="The order's revision on the server">update {view.revision}</span> : null}
    />
  );
}

function PhonePanel({ view, restaurant }: { view: View | null; restaurant: string }) {
  const lines = view?.phone.lines ?? [];
  const stage = view?.phone.stage ?? null;
  const machine = view?.phone.machine ?? [
    { label: "Cart", stage: "cart" as Stage, at: "ahead" as const },
    { label: "Ordered", stage: "sent" as Stage, at: "ahead" as const },
    { label: "Delivered", stage: "delivered" as Stage, at: "ahead" as const },
  ];
  const box = useRef<HTMLDivElement>(null);
  // Scroll the list itself, never the page.
  useLayoutEffect(() => {
    if (box.current) box.current.scrollTop = box.current.scrollHeight;
  }, [lines.length]);

  return (
    <div className="phone-wrap">
      <div className="phone" aria-label="Phone">
        <div className="phone-notch" />
        <div className="phone-screen">
          <div className="phone-head">
            <p className="group-label">Your order</p>
            <p className="phone-restaurant">{restaurant}</p>
            <p className="phone-status" style={{ color: stage ? STAGES[stage].color : undefined }}>
              {view?.phone.status ?? "Nothing yet"}
            </p>
            <p className="phone-detail">{view?.phone.detail}</p>
            <ol className="phone-dots" aria-label="The order's states">
              {machine.map((m, i) => (
                <li key={m.label}>
                  {i > 0 && <span className="phone-dash" />}
                  <span
                    className="phone-dot"
                    style={m.at === "ahead" ? undefined : { borderColor: STAGES[m.stage].color, background: m.at === "current" ? STAGES[m.stage].color : tint(m.stage, 0.3) }}
                  />
                  <span style={m.at === "current" ? { color: STAGES[m.stage].color } : undefined}>{m.label}</span>
                </li>
              ))}
            </ol>
          </div>
          <div ref={box} className="phone-lines">
            {lines.map((l) => (
              <div key={l.key} className="phone-line">
                <span className="phone-bullet" style={{ background: STAGES[l.stage].color }} />
                <span className="phone-text">{l.text}</span>
                <span className="phone-clock">{l.clock}</span>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

/** The timeline as a fixed grid: the row names, then Cart, the five junctions under Ordered, then the outcomes. */
function OrderTimeline({ track }: { track: Track }) {
  const [cart, ...rest] = track.machine;
  const ordered = rest.find((s) => s.key === "Ordered");
  const outcomes = rest.filter((s) => s.key !== "Ordered");
  return (
    <div className="panel timeline">
      <div className="panel-title">Timeline</div>
      <div className="timeline-stacked">
        <div className="stacked-lane">
          <span className="lane-name">OrderMachine</span>
          <OrderSlot slot={cart} />
          {track.timer && <div className={`stacked-timer ${track.timer.lit ? "lit" : ""}`}>{track.timer.text}</div>}
          {ordered && <OrderSlot slot={ordered} />}
          {track.train && (
            <div className="stacked-train">
              {track.starts && <span className={`link-text ${track.starts.lit ? "lit" : ""}`}>starts the train</span>}
              <span className="lane-name">DeliverOrderTrain</span>
              {track.train.map((s) => (
                <OrderSlot key={s.key} slot={s} />
              ))}
              {track.outcome && <span className={`link-text ${track.outcome.lit ? "lit" : ""} ${track.outcome.tone}`}>{track.outcome.text}</span>}
            </div>
          )}
          <div className="action-pair">
            {outcomes.map((s) => (
              <OrderSlot key={s.key} slot={s} />
            ))}
          </div>
        </div>
      </div>
      <div className="timeline-grid">
        <div className="order-grid-wrap">
          <div className="order-grid">
            <span className="lane-name">OrderMachine</span>
            <OrderSlot slot={cart} />
            {ordered ? (
              <div className="span-5">
                <OrderSlot slot={ordered} />
              </div>
            ) : (
              <div className="span-5 timer-cell">
                {track.timer && (
                  <div className={`timer-line ${track.timer.lit ? "lit" : ""}`}>
                    <span>{track.timer.text}</span>
                  </div>
                )}
              </div>
            )}
            {outcomes.map((s) => (
              <OrderSlot key={s.key} slot={s} />
            ))}
          </div>
          {track.train && (
            <>
              <div className="order-grid connectors">
                <div className="connector-cell start">
                  {track.starts && (
                    <Connector lit={track.starts.lit} side="left" tone="done">
                      starts the train
                    </Connector>
                  )}
                </div>
                <div className="connector-cell end">
                  {track.outcome && (
                    <Connector lit={track.outcome.lit} side="right" tone={track.outcome.tone}>
                      {track.outcome.text}
                    </Connector>
                  )}
                </div>
              </div>
              <div className="order-grid">
                <span className="lane-name">DeliverOrderTrain</span>
                <div />
                {track.train.map((s) => (
                  <OrderSlot key={s.key} slot={s} />
                ))}
              </div>
            </>
          )}
        </div>
      </div>
    </div>
  );
}

function OrderSlot({ slot }: { slot: Slot }) {
  // Empty and muted until reached; then the stage's quiet tint fills it left to right. A failed try is the colour of
  // a cancelled order.
  const stage: Stage = slot.tone === "failed" ? "cancelled" : slot.stage;
  const on = slot.tone !== "idle";
  return (
    <div
      title={slot.title ?? slot.label}
      className={`slot ${slot.dimmed ? "dimmed" : ""}`}
      style={on ? { borderColor: tint(stage, 0.55), background: slot.tone === "waiting" ? tint(stage, 0.07) : undefined } : undefined}
    >
      <div className="slot-fill" style={{ width: `${slot.fill * 100}%`, background: on ? tint(stage, 0.24) : undefined }} />
      <span className="slot-text" style={on ? { color: STAGES[stage].color } : undefined}>
        <span className="slot-label">{slot.label}</span>
        {slot.attempt && <span className="slot-note">attempt {slot.attempt}</span>}
      </span>
    </div>
  );
}

// A thin vertical line between the machine's row and the train's, with a small label beside it: muted until it happens.
function Connector({ lit, side, tone, children }: { lit: boolean; side: "left" | "right"; tone: Tone; children: React.ReactNode }) {
  return (
    <>
      <div className={`connector-line ${side} ${lit ? "lit" : ""} ${tone}`} />
      <span className={`connector-text ${side} ${lit ? "lit" : ""} ${tone}`}>{children}</span>
    </>
  );
}
