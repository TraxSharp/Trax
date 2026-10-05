"use client";

import { useLayoutEffect, useMemo, useRef, useState } from "react";
import { useFitCode } from "./useFitCode";
import data from "@/data/state-machine-recordings.json";
import {
  availability,
  configureRange,
  lineOf,
  open,
  press,
  queryOf,
  snapshotOf,
  type LogEntry,
  type MachineName,
  type Recordings,
  type Session,
} from "@/lib/state-machine-replay";

// Recorded from the StateMachine sample by Trax.Samples' scripts/recordings/state-machine.mjs, which writes this
// file with its --copy-to option. Re-record when the sample's machines or its page change.
const recordings = data as unknown as Recordings;

interface Move {
  trigger: string;
  from: string;
  to: string;
  note?: string;
}

// The machines as the sample's page draws them (web/src/machines.ts). Display only: every answer is the server's.
const MACHINES: Record<MachineName, { label: string; example: string; states: string[]; moves: Move[] }> = {
  turnstile: {
    label: "Turnstile",
    example: "A coin unlocks it, walking through locks it again. Only a quarter or a dollar is accepted.",
    states: ["Locked", "Unlocked"],
    moves: [
      { trigger: "Coin", from: "Locked", to: "Unlocked", note: "guard" },
      { trigger: "Push", from: "Unlocked", to: "Locked" },
    ],
  },
  checkout: {
    label: "Checkout",
    example: "Fill a cart, review it, pay. Paying charges a card, which must never happen twice.",
    states: ["Cart", "Review", "Paid"],
    moves: [
      { trigger: "Next", from: "Cart", to: "Review" },
      { trigger: "Back", from: "Review", to: "Cart" },
      { trigger: "Pay", from: "Review", to: "Paid", note: "charge, once" },
      { trigger: "Reset", from: "Paid", to: "Cart" },
    ],
  },
};

const BUTTONS: Record<string, { label: string; caption: string; trigger?: string; probe?: boolean }> = {
  "coin-quarter": { label: "Coin { coin: quarter }", caption: "insert a quarter", trigger: "Coin" },
  "coin-dollar": { label: "Coin { coin: dollar }", caption: "insert a dollar", trigger: "Coin" },
  "coin-penny": { label: "Coin { coin: penny }", caption: "insert a penny", trigger: "Coin", probe: true },
  push: { label: "Push", caption: "walk through", trigger: "Push" },
  next: { label: "Next", caption: "go to review", trigger: "Next" },
  back: { label: "Back", caption: "back to the cart", trigger: "Back" },
  pay: { label: "Pay", caption: "sendSnapshot: runs the charge", trigger: "Pay" },
  reset: { label: "Reset", caption: "start over", trigger: "Reset" },
  "add-item": { label: "Add an item", caption: "saveSnapshot at $9.99 each" },
  "save-penny-total": { label: "Save with a $0.01 total", caption: "saveSnapshot, wrong total", probe: true },
};

export default function StateMachineReplay() {
  const [sessions, setSessions] = useState<Record<MachineName, Session>>(() => ({
    turnstile: open(recordings, "turnstile"),
    checkout: open(recordings, "checkout"),
  }));
  const [machine, setMachine] = useState<MachineName>("turnstile");
  const session = sessions[machine];
  const snapshot = snapshotOf(recordings, session);
  const view = MACHINES[machine];
  const last = session.log.at(-1);
  const pressed = last?.action ? last : undefined;

  const act = (id: string) => setSessions((all) => ({ ...all, [machine]: press(recordings, all[machine], id) }));
  const startOver = () => setSessions((all) => ({ ...all, [machine]: open(recordings, machine) }));

  return (
    <div className="grid gap-4 lg:grid-cols-[230px_minmax(0,1fr)_minmax(0,300px)]">
      {/* Controls */}
      <div className="flex flex-col gap-5 rounded-lg border border-border bg-bg-secondary p-4 text-sm">
        <Group label="Machine">
          <div role="tablist" className="grid grid-cols-2 gap-0.5 rounded-md border border-border bg-bg-primary p-0.5">
            {(Object.keys(MACHINES) as MachineName[]).map((name) => (
              <button
                key={name}
                role="tab"
                aria-selected={machine === name}
                onClick={() => setMachine(name)}
                className={`rounded px-2 py-1.5 text-xs font-medium transition-colors ${
                  machine === name ? "bg-bg-tertiary text-text-primary" : "text-text-muted hover:text-text-secondary"
                }`}
              >
                {MACHINES[name].label}
              </button>
            ))}
          </div>
          <p className="text-xs leading-relaxed text-text-muted">{view.example}</p>
        </Group>

        <Group label="Fire a trigger">
          <div className="flex flex-col gap-1.5">
            {recordings.actions[machine].map((action) => {
              const button = BUTTONS[action.id];
              const can = availability(recordings, session, action.id);
              const arrow = view.moves.some((m) => m.trigger === button.trigger && m.from === snapshot.state);
              return (
                <button
                  key={action.id}
                  onClick={() => act(action.id)}
                  disabled={!can.ok}
                  title={can.reason}
                  className={`rounded-md border px-3 py-1.5 text-left transition-colors disabled:cursor-default disabled:opacity-35 ${
                    arrow
                      ? "border-accent/60 bg-accent/10 hover:border-accent-bright"
                      : "border-border bg-bg-tertiary hover:border-border-hover"
                  }`}
                >
                  <span className={`block font-mono text-xs ${button.probe ? "text-derail" : "text-text-primary"}`}>
                    {button.label}
                  </span>
                  <span className="block text-[11px] text-text-muted">{button.caption}</span>
                </button>
              );
            })}
          </div>
          <p className="text-xs leading-relaxed text-text-muted">
            Every trigger can be sent from any state. The green ones have an arrow out of {snapshot.state}; the
            others show what the server says when there isn&apos;t one.
          </p>
          <button
            onClick={startOver}
            disabled={session.log.length <= 2}
            className="rounded-md border border-border bg-bg-tertiary px-3 py-2 text-left text-sm text-text-primary transition-colors hover:border-accent/60 disabled:cursor-default disabled:opacity-35"
          >
            Start over
          </button>
        </Group>
      </div>

      {/* The machine, and the code that declares it */}
      <div className="flex min-w-0 flex-col gap-4">
        <Diagram states={view.states} moves={view.moves} current={snapshot.state} last={pressed} />
        <CodePanel machine={machine} entry={pressed} />
      </div>

      {/* What the server did */}
      <div className="flex min-w-0 flex-col gap-4">
        <Verdict entry={pressed} state={snapshot.state} context={snapshot.context} />
        {machine === "checkout" && <Provider session={session} entry={pressed} />}
      </div>

      <Traffic log={session.log} />
    </div>
  );
}

function Group({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-2.5">
      <span className="text-[11px] font-semibold uppercase tracking-wider text-text-muted">{label}</span>
      {children}
    </div>
  );
}

function Panel({ title, extra, children, className = "" }: { title: string; extra?: React.ReactNode; children: React.ReactNode; className?: string }) {
  return (
    <div className={`flex min-w-0 flex-col overflow-hidden rounded-lg border border-border bg-bg-secondary ${className}`}>
      <div className="flex items-center gap-3 border-b border-border px-4 py-2 text-[11px] font-semibold uppercase tracking-wider text-text-muted">
        {title}
        {extra}
      </div>
      {children}
    </div>
  );
}

function Diagram({ states, moves, current, last }: { states: string[]; moves: Move[]; current: string; last?: LogEntry }) {
  const moved = last && !last.problem && last.from !== current ? { from: last.from, to: current } : null;
  return (
    <Panel title="State" extra={<span className="font-mono normal-case tracking-normal text-accent-bright">{current}</span>}>
      <div className="flex flex-wrap items-center gap-2 px-4 py-4">
        {states.map((state) => (
          <span
            key={state}
            className={`rounded-md border px-3 py-1.5 font-mono text-sm transition-colors ${
              state === current ? "border-accent-bright bg-accent/20 text-text-primary" : "border-border text-text-muted"
            }`}
          >
            {state}
          </span>
        ))}
      </div>
      <ul className="flex flex-wrap gap-x-4 gap-y-1 border-t border-border px-4 py-2.5 font-mono text-[11.5px]">
        {moves.map((m) => {
          const lit = moved?.from === m.from && moved?.to === m.to;
          const available = m.from === current;
          return (
            <li key={`${m.trigger}-${m.from}`} className={lit ? "text-signal" : available ? "text-accent-bright" : "text-text-muted"}>
              {m.from} <span className="opacity-70">&rarr;{m.trigger}&rarr;</span> {m.to}
              {m.note && <span className="ml-1 text-text-muted">({m.note})</span>}
            </li>
          );
        })}
      </ul>
    </Panel>
  );
}

function CodePanel({ machine, entry }: { machine: MachineName; entry?: LogEntry }) {
  const { file, code } = recordings.source;
  const lines = useMemo(() => code.split("\n"), [code]);
  const [first, last] = useMemo(() => configureRange(code, machine), [code, machine]);
  const indent = useMemo(
    () => Math.min(...lines.slice(first, last).filter((l) => l.trim()).map((l) => l.search(/\S/))),
    [lines, first, last],
  );
  const pane = useRef<HTMLPreElement>(null);
  useFitCode(pane, machine);
  const highlighted = lineOf(code, machine, entry);
  const tone = entry?.problem ? "border-derail bg-derail/15" : entry?.exchange.op === "sendSnapshot" ? "border-signal bg-signal/15" : "border-accent-bright bg-accent/15";

  return (
    <Panel title="Code" extra={<span className="font-mono normal-case tracking-normal text-text-secondary">{file}</span>} className="flex-1">
      <pre ref={pane} className="flex-1 overflow-x-auto py-3 font-mono text-[12px] leading-[1.7] text-text-secondary">
        {lines.slice(first, last).map((line, offset) => {
          const i = first + offset;
          return (
            <div key={i} className={`flex w-max min-w-full border-l-2 pr-4 transition-colors duration-200 ${i === highlighted ? tone : "border-transparent"}`}>
              <span className="mr-4 w-8 shrink-0 select-none text-right text-text-muted/60">{i + 1}</span>
              <span className="whitespace-pre">{highlight(line.slice(indent))}</span>
            </div>
          );
        })}
      </pre>
    </Panel>
  );
}

const TOKEN = /(\.(?:On|When|Because|Holds|RunsOnce|Reduce|To|Committed|MigrateFrom|StartsAt)\b)|(m\.In)|("[^"]*")/g;

function highlight(line: string) {
  const parts: React.ReactNode[] = [];
  let last = 0;
  for (const m of line.matchAll(TOKEN)) {
    if (m.index > last) parts.push(line.slice(last, m.index));
    const tone = m[1] ? "text-signal" : m[2] ? "text-info" : "text-accent-bright";
    parts.push(
      <span key={m.index} className={tone}>
        {m[0]}
      </span>,
    );
    last = m.index + m[0].length;
  }
  parts.push(line.slice(last));
  return parts;
}

function Verdict({ entry, state, context }: { entry?: LogEntry; state: string; context: Record<string, unknown> }) {
  return (
    <Panel title="What the server did">
      <div className="space-y-3 px-4 py-3 text-sm">
        {!entry ? (
          <p className="text-text-muted">Fire a trigger. The server checks it against the machine and answers.</p>
        ) : entry.problem ? (
          <div>
            <p className="font-semibold text-derail">Refused: {entry.problem.code}</p>
            <p className="mt-1 text-text-secondary">{entry.problem.message} Nothing was stored.</p>
          </div>
        ) : entry.replayed ? (
          <div>
            <p className="font-semibold text-signal">The charge had already run</p>
            <p className="mt-1 text-text-secondary">
              The server returned the first Pay&apos;s receipt and charged nothing.
            </p>
          </div>
        ) : (
          <div>
            <p className="font-semibold text-accent-bright">
              {entry.from !== state ? `Moved ${entry.from} to ${state}` : `Stored in ${state}`}
            </p>
            <p className="mt-1 text-text-secondary">
              {entry.exchange.op === "sendSnapshot"
                ? "The page sent only the draft's id. The server ran the charge itself, exactly once."
                : entry.exchange.op === "saveSnapshot"
                  ? "The server checked the page's draft against the state's rule before keeping it."
                  : "The server ran the move on its own stored copy of the draft."}
            </p>
          </div>
        )}
        <div className="border-t border-border pt-3">
          <p className="text-[11px] font-semibold uppercase tracking-wider text-text-muted">Context</p>
          <p className="mt-1 font-mono text-xs break-all text-text-secondary">{JSON.stringify(context)}</p>
        </div>
      </div>
    </Panel>
  );
}

function Provider({ session, entry }: { session: Session; entry?: LogEntry }) {
  const fresh = entry?.charges[0]?.receipt;
  return (
    <Panel
      title="Payment provider"
      extra={<span className="ml-auto normal-case tracking-normal">{session.charges.length} charge{session.charges.length === 1 ? "" : "s"}</span>}
    >
      <div className="px-4 py-3 text-sm">
        {entry?.replayed && <p className="mb-2 text-xs text-signal">Pay again: no new charge.</p>}
        {session.charges.length === 0 ? (
          <p className="text-xs text-text-muted">No charges yet. Add an item, go to Review and press Pay.</p>
        ) : (
          <ul className="space-y-1.5">
            {session.charges.map((c) => (
              <li
                key={c.receipt}
                className={`flex flex-wrap items-baseline gap-x-3 rounded border px-2 py-1 font-mono text-xs ${
                  c.receipt === fresh ? "border-accent/60 bg-accent/10" : "border-border"
                }`}
              >
                <span className="text-text-primary">${(c.amountCents / 100).toFixed(2)}</span>
                <span className="text-text-muted">
                  {c.items} item{c.items === 1 ? "" : "s"}
                </span>
                <span className="truncate text-text-muted">{c.receipt.slice(0, 18)}…</span>
              </li>
            ))}
          </ul>
        )}
        <p className="mt-3 text-[11px] leading-relaxed text-text-muted">
          The page never calls a payment API: the charge is bound to the Pay move with{" "}
          <code className="text-text-secondary">RunsOnce&lt;ICharge&gt;</code>.
        </p>
      </div>
    </Panel>
  );
}

function Traffic({ log }: { log: LogEntry[] }) {
  const box = useRef<HTMLDivElement>(null);
  // Scroll the panel itself, never the page.
  useLayoutEffect(() => {
    if (box.current) box.current.scrollTop = box.current.scrollHeight;
  }, [log.length]);

  return (
    <Panel title="GraphQL traffic" extra={<span className="normal-case tracking-normal">the requests the page sent, and the server&apos;s answers</span>} className="lg:col-span-3">
      <div ref={box} className="max-h-[340px] space-y-2 overflow-y-auto px-4 py-3">
        {log.map((entry) => (
          <details key={entry.key} open={entry.key === log.length - 1} className="rounded border border-border bg-bg-primary/60">
            <summary className="flex cursor-pointer flex-wrap items-baseline gap-x-3 px-3 py-1.5 font-mono text-xs">
              <span className="text-text-primary">
                POST {entry.exchange.op} · {String(entry.exchange.variables.i.machine)}
                {entry.exchange.variables.i.trigger ? ` · ${String(entry.exchange.variables.i.trigger)}` : ""}
              </span>
              <span className={entry.problem ? "text-derail" : "text-accent-bright"}>
                {entry.problem ? `Refused · ${entry.problem.code}` : `200 OK · ${entry.snapshot?.state ?? ""}`}
              </span>
              <span className="text-text-muted">{entry.exchange.ms} ms</span>
            </summary>
            <div className="grid gap-2 border-t border-border p-3 lg:grid-cols-2">
              <Json title="Request" value={{ query: queryOfEntry(entry), variables: entry.exchange.variables }} />
              <Json title="Response" value={entry.exchange.response} />
            </div>
          </details>
        ))}
      </div>
    </Panel>
  );
}

const queryOfEntry = (entry: LogEntry) => queryOf(recordings, entry.exchange);

function Json({ title, value }: { title: string; value: unknown }) {
  return (
    <div className="min-w-0">
      <p className="text-[11px] font-semibold uppercase tracking-wider text-text-muted">{title}</p>
      <pre className="mt-1 overflow-x-auto whitespace-pre-wrap break-all font-mono text-[11px] leading-relaxed text-text-secondary">
        {JSON.stringify(value, null, 2)}
      </pre>
    </div>
  );
}
