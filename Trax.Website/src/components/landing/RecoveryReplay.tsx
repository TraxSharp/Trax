"use client";

import Link from "next/link";
import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { useFitCode } from "./useFitCode";
import data from "@/data/recovery-recordings.json";
import {
  advance,
  answerMs,
  badgeOf,
  begin,
  currentStep,
  keyOf,
  labelOf,
  lineOf,
  phaseOf,
  requeue,
  takeFork,
  type Attempt,
  type Line,
  type Playback,
  type Recording,
  type Scenario,
  type Step,
} from "@/lib/recovery-replay";

// Recorded from the Recovery sample by Trax.Samples' scripts/recordings, which writes this file with its
// --copy-to option. Re-record when the sample's trains or its page change.
const recordings = data.recordings as Recording[];
const sources = data.sources as Record<Scenario, { file: string; code: string }>;

const ORDERS = [
  { key: "A-1001", amount: "$89.00", reason: "Arrived broken", priorRefunds: 0 },
  { key: "A-1002", amount: "$420.00", reason: "Never arrived", priorRefunds: 0 },
  { key: "A-1003", amount: "$35.50", reason: "Changed my mind", priorRefunds: 2 },
];
const TOPICS = [
  { key: "papers", label: "What the papers say about cold-weather battery wear" },
  { key: "wiki", label: "History of the telegraph" },
];

/** One stop on a train's route: a junction, or a question whose answer picks one of its tracks. */
type Stop =
  | { kind: "junction"; name: string }
  | { kind: "question"; key: string; label: string; tracks: { answer: string; junction: string }[] };

// The shape of each train's chain, as its Junctions() declares it, so the map can show the tracks a run did not take.
const ROUTES: Record<Scenario, Stop[]> = {
  REFUND: [
    { kind: "junction", name: "LoadRefundCase" },
    {
      kind: "question",
      key: "ApproveRefund",
      label: "Pay without review?",
      tracks: [
        { answer: "Yes", junction: "IssuePayment" },
        { answer: "Unsure", junction: "QueueForReview" },
        { answer: "No", junction: "DeclineRefund" },
      ],
    },
    { kind: "junction", name: "NotifyCustomer" },
  ],
  RESEARCH: [
    { kind: "junction", name: "PlanResearch" },
    {
      kind: "question",
      key: "Source",
      label: "Where to look?",
      tracks: [
        { answer: "Web", junction: "SearchWeb" },
        { answer: "Papers", junction: "SearchPapers" },
        { answer: "Wiki", junction: "SearchWiki" },
      ],
    },
    {
      kind: "question",
      key: "Depth",
      label: "How deep?",
      tracks: [
        { answer: "Skim", junction: "SkimSources" },
        { answer: "CrossCheck", junction: "FetchFullTexts" },
      ],
    },
    { kind: "junction", name: "Summarize" },
  ],
};

const questionsOf = (s: Scenario) => ROUTES[s].filter((x): x is Extract<Stop, { kind: "question" }> => x.kind === "question");
const junctionStep = (a: Attempt, name: string) => Object.values(a.steps).find((s) => s.kind === "JUNCTION" && s.name === name);
const questionStep = (a: Attempt, key: string) =>
  Object.values(a.steps).find((s) => s.kind !== "JUNCTION" && s.kind !== "ROUTE" && s.questionKey === key);
const routeStep = (a: Attempt, key: string) => Object.values(a.steps).find((s) => s.kind === "ROUTE" && s.questionKey === key);
const seconds = (ms: number) => (ms >= 1000 ? `${(ms / 1000).toFixed(1)} s` : `${Math.round(ms)} ms`);

export default function RecoveryReplay() {
  const [scenario, setScenario] = useState<Scenario>("REFUND");
  const [topic, setTopic] = useState(TOPICS[0].key);
  const [order, setOrder] = useState(ORDERS[0].key);
  const [crash, setCrash] = useState(true);
  const [tab, setTab] = useState<"source" | "events">("source");
  const [playback, setPlayback] = useState<Playback | null>(null);
  const [elapsed, setElapsed] = useState(0);
  // Playback time is performance.now() minus this.
  const base = useRef(0);

  const phase = phaseOf(playback);
  const active = playback != null && !playback.waiting && !playback.finished;
  const locked = active && phase !== "done";

  useEffect(() => {
    if (!active) return;
    const timer = setInterval(() => {
      const now = performance.now() - base.current;
      setElapsed(now);
      setPlayback((p) => (p ? advance(p, now) : p));
    }, 50);
    return () => clearInterval(timer);
  }, [active]);

  const run = useCallback(
    (s: Scenario = scenario, subject: string = s === "RESEARCH" ? topic : order, withCrash: boolean = crash) => {
      const recording = recordings.find((r) => r.key === keyOf(s, subject, withCrash));
      if (!recording) return;
      base.current = performance.now();
      setElapsed(0);
      setPlayback(begin(recording));
    },
    [scenario, topic, order, crash],
  );

  const fork = (which: "changeData" | "askAfresh") => {
    if (!playback) return;
    const taken = takeFork(playback, recordings, which);
    if (!taken) return;
    base.current -= taken.shift;
    setPlayback(taken.playback);
  };

  const askAfresh = () => {
    if (!playback) return;
    if (phase === "backoff") return fork("askAfresh");
    const asked = requeue(playback);
    if (!asked) return;
    base.current = performance.now() - asked.at;
    setElapsed(asked.at);
    setPlayback(asked.playback);
  };

  // Changing what to run clears the last run.
  const choose = (change: () => void) => {
    change();
    setPlayback(null);
  };

  // Play the default run once, the first time the demo is on screen, unless the reader prefers less motion.
  const root = useRef<HTMLDivElement>(null);
  const autoplayed = useRef(false);
  useEffect(() => {
    const node = root.current;
    if (!node || typeof IntersectionObserver === "undefined") return;
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    const observer = new IntersectionObserver(
      ([entry]) => {
        if (!entry.isIntersecting || autoplayed.current) return;
        autoplayed.current = true;
        observer.disconnect();
        run("REFUND", ORDERS[0].key, true);
      },
      { threshold: 0.2 },
    );
    observer.observe(node);
    return () => observer.disconnect();
  }, [run]);

  const shown = playback?.recording.scenario ?? scenario;
  const changed = playback?.forkTaken === "changeData";
  const step = playback ? currentStep(playback) : null;

  return (
    <div ref={root} className="grid gap-4 lg:grid-cols-[250px_minmax(0,1fr)]">
      <CasePanel
        scenario={scenario}
        topic={topic}
        order={order}
        crash={crash}
        locked={locked}
        changed={changed && shown === scenario}
        onScenario={(s) => choose(() => setScenario(s))}
        onTopic={(t) => choose(() => setTopic(t))}
        onOrder={(o) => choose(() => setOrder(o))}
        onCrash={(c) => choose(() => setCrash(c))}
        onRun={() => run()}
        onClear={() => setPlayback(null)}
        canClear={!!playback && !locked}
      />

      <div className="flex min-w-0 flex-col gap-4">
        <Moment
          playback={playback}
          scenario={shown}
          elapsed={elapsed}
          onChangeData={() => fork("changeData")}
          onAskAfresh={askAfresh}
        />
        <Decisions scenario={shown} playback={playback} />
        <RouteMap scenario={shown} playback={playback} />
      </div>

      <div className="flex min-w-0 flex-col overflow-hidden rounded-lg border border-border bg-bg-secondary lg:col-span-2">
        <div role="tablist" className="flex gap-1 border-b border-border px-2">
          {(["source", "events"] as const).map((t) => (
            <button
              key={t}
              role="tab"
              aria-selected={tab === t}
              onClick={() => setTab(t)}
              className={`-mb-px border-b-2 px-3 py-2 text-sm font-medium transition-colors ${
                tab === t ? "border-accent text-text-primary" : "border-transparent text-text-muted hover:text-text-secondary"
              }`}
            >
              {t === "source" ? "Train source" : "Junction events"}
            </button>
          ))}
        </div>
        {tab === "source" ? (
          <CodePanel source={sources[shown]} step={playback?.recording.scenario === shown ? step : null} />
        ) : (
          <EventFeed playback={playback} />
        )}
      </div>

      <p className="text-xs leading-relaxed text-text-muted lg:col-span-2">
        Recorded from the{" "}
        <Link href="/docs/samples/recovery" className="text-accent hover:text-accent-hover">
          Recovery sample
        </Link>{" "}
        running against Postgres, and played back in your browser: every junction, answer and track arrived as a{" "}
        <Link href="/docs/effect/junction-events" className="text-accent hover:text-accent-hover">
          junction event
        </Link>
        . The model is the sample&apos;s stand-in, which answers like a typed decision model; one setting points it at{" "}
        <Link href="/docs/sdk-reference/configuration/add-nimble-decider" className="text-accent hover:text-accent-hover">
          Nimble
        </Link>{" "}
        instead.
      </p>
    </div>
  );
}

function CasePanel(props: {
  scenario: Scenario;
  topic: string;
  order: string;
  crash: boolean;
  locked: boolean;
  changed: boolean;
  onScenario(s: Scenario): void;
  onTopic(t: string): void;
  onOrder(o: string): void;
  onCrash(c: boolean): void;
  onRun(): void;
  onClear(): void;
  canClear: boolean;
}) {
  const { scenario, locked, changed } = props;
  const order = ORDERS.find((o) => o.key === props.order) ?? ORDERS[0];
  const topic = TOPICS.find((t) => t.key === props.topic) ?? TOPICS[0];
  return (
    <div className="flex flex-col gap-4 rounded-lg border border-border bg-bg-secondary p-4 text-sm">
      <div role="tablist" aria-label="Train" className="grid grid-cols-2 gap-1 rounded-md bg-bg-primary p-1">
        {(["REFUND", "RESEARCH"] as Scenario[]).map((s) => (
          <button
            key={s}
            role="tab"
            aria-selected={scenario === s}
            disabled={locked}
            onClick={() => props.onScenario(s)}
            className={`rounded px-2 py-1.5 text-xs font-medium transition-colors disabled:cursor-default ${
              scenario === s ? "bg-bg-tertiary text-text-primary" : "text-text-muted hover:text-text-secondary"
            }`}
          >
            {s === "REFUND" ? "Refund approval" : "Research brief"}
          </button>
        ))}
      </div>

      {scenario === "REFUND" ? (
        <div role="radiogroup" aria-label="Order" className="flex flex-col gap-1.5">
          {ORDERS.map((o) => (
            <button
              key={o.key}
              role="radio"
              aria-checked={o.key === props.order}
              disabled={locked}
              onClick={() => props.onOrder(o.key)}
              className={`grid grid-cols-[auto_1fr] gap-x-3 rounded-md border px-3 py-2 text-left transition-colors disabled:cursor-default ${
                o.key === props.order ? "border-accent bg-accent-muted/40" : "border-border bg-bg-primary hover:border-border-hover"
              } ${locked && o.key !== props.order ? "opacity-50" : ""}`}
            >
              <span className="font-mono text-xs font-semibold text-text-primary">{o.key}</span>
              <span className="justify-self-end text-xs font-semibold text-text-primary">{o.amount}</span>
              <span className="col-span-2 text-xs text-text-muted">{o.reason}</span>
            </button>
          ))}
        </div>
      ) : (
        <label className="flex flex-col gap-1.5 text-xs text-text-muted">
          Topic
          <select
            value={props.topic}
            disabled={locked}
            onChange={(e) => props.onTopic(e.target.value)}
            className="rounded-md border border-border bg-bg-primary px-2 py-2 text-sm text-text-primary outline-none focus:border-accent disabled:opacity-50"
          >
            {TOPICS.map((t) => (
              <option key={t.key} value={t.key}>
                {t.label}
              </option>
            ))}
          </select>
        </label>
      )}

      <div className="rounded-md border border-dashed border-border-hover px-3 py-2.5">
        <div className="mb-2 flex items-baseline justify-between gap-2 text-[10.5px] font-semibold uppercase tracking-wider text-text-muted">
          What the model sees
          <span className="font-normal normal-case tracking-normal">hashed with each answer</span>
        </div>
        <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-xs">
          {scenario === "REFUND" ? (
            <>
              <dt className="text-text-muted">Amount</dt>
              <dd className="text-right text-text-primary">{order.amount}</dd>
              <dt className="text-text-muted">Reason</dt>
              <dd className="text-right text-text-primary">{order.reason}</dd>
              <dt className="text-text-muted">Earlier refunds</dt>
              <Changed changed={changed} before={String(order.priorRefunds)} after={String(order.priorRefunds + 1)} />
              <dt className="text-text-muted">Email</dt>
              <dd className="text-right text-text-muted">sensitive, hashed with a key</dd>
            </>
          ) : (
            <>
              <dt className="text-text-muted">Topic</dt>
              <dd className="text-right text-text-primary">{topic.label}</dd>
              <dt className="text-text-muted">Audience</dt>
              <Changed changed={changed} before="engineers" after="executives" />
            </>
          )}
        </dl>
      </div>

      <label className="flex cursor-pointer items-start gap-2.5 text-xs text-text-secondary">
        <input
          type="checkbox"
          checked={props.crash}
          disabled={locked}
          onChange={(e) => props.onCrash(e.target.checked)}
          className="mt-0.5 h-4 w-4 accent-derail"
        />
        <span>
          Crash the first attempt
          <span className="block text-text-muted">
            {scenario === "REFUND" ? "in the step the approval routes to" : "while it writes the report"}
          </span>
        </span>
      </label>

      <div className="grid grid-cols-[1fr_auto] gap-2">
        <button
          onClick={props.onRun}
          disabled={locked}
          className="rounded-md bg-accent px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-accent-hover disabled:cursor-default disabled:opacity-40"
        >
          Run
        </button>
        <ActionButton onClick={props.onClear} disabled={!props.canClear}>
          Clear
        </ActionButton>
      </div>
    </div>
  );
}

function Changed({ changed, before, after }: { changed: boolean; before: string; after: string }) {
  return changed ? (
    <dd className="text-right font-semibold text-afresh">
      <s className="mr-1 font-normal text-text-muted">{before}</s>
      {after}
    </dd>
  ) : (
    <dd className="text-right text-text-primary">{before}</dd>
  );
}

function ActionButton({ children, ...props }: React.ButtonHTMLAttributes<HTMLButtonElement>) {
  return (
    <button
      {...props}
      className="rounded-md border border-border bg-bg-tertiary px-3 py-2 text-sm text-text-primary transition-colors hover:border-accent/60 disabled:cursor-default disabled:opacity-35 disabled:hover:border-border"
    >
      {children}
    </button>
  );
}

/** Where the run is, in one sentence, and during the backoff the choice the reader has. */
function Moment({
  playback,
  scenario,
  elapsed,
  onChangeData,
  onAskAfresh,
}: {
  playback: Playback | null;
  scenario: Scenario;
  elapsed: number;
  onChangeData(): void;
  onAskAfresh(): void;
}) {
  const phase = phaseOf(playback);
  const attempts = playback?.attempts ?? [];

  if (playback && phase === "backoff" && playback.forkTaken === "none") {
    const failed = [...attempts].reverse().find((a) => a.trainState === "FAILED");
    const next = playback.recording.events.slice(playback.cursor).find((e) => e.type === "attempt");
    const left = next ? Math.max(0, next.t - elapsed) : 0;
    const total = next && failed?.end != null ? Math.max(1, next.t - failed.end) : 1;
    const reuse = failed ? questionsOf(scenario).filter((q) => questionStep(failed, q.key)).length : 0;
    return (
      <div className="relative overflow-hidden rounded-lg border border-signal/50 bg-signal-muted/40 p-4">
        <p className="text-sm font-semibold text-text-primary">
          {failed?.failureJunction ?? "A junction"} crashed. The retry starts in {(left / 1000).toFixed(1)} s and will
          reuse {reuse} recorded answer{reuse === 1 ? "" : "s"}.
        </p>
        <p className="mt-1 text-sm text-text-secondary">
          Before it does, you can change what the model would see, or ask it again on purpose. Or do nothing.
        </p>
        <div className="mt-3 flex flex-wrap gap-2">
          <ActionButton onClick={onChangeData}>
            {scenario === "REFUND" ? "Add an earlier refund to the order" : "Make the brief for executives"}
          </ActionButton>
          <ActionButton onClick={onAskAfresh}>Ask the model again</ActionButton>
        </div>
        <div className="absolute bottom-0 left-0 h-[3px] bg-signal" style={{ width: `${Math.min(100, (left / total) * 100)}%` }} />
      </div>
    );
  }

  if (playback && phase === "done") {
    const manifest = attempts.filter((a) => a.origin === "manifest");
    const replayed = manifest.flatMap((a) => questionsOf(scenario).map((q) => questionStep(a, q.key))).filter((s) => s?.replayed).length;
    const text =
      manifest.length > 1
        ? replayed > 0
          ? `Recovered. The retry reused ${replayed} answer${replayed === 1 ? "" : "s"} and took the same tracks.`
          : "Recovered. The retry asked the model again, so its tracks follow the new answers."
        : "Completed on the first attempt.";
    return (
      <div className="flex flex-wrap items-center gap-3 rounded-lg border border-accent/50 bg-accent-muted/30 p-4">
        <div className="min-w-[240px] flex-1">
          <p className="text-sm font-semibold text-text-primary">{text}</p>
          <p className="mt-1 text-sm text-text-secondary">
            {playback.requeued
              ? "The re-run put every question to the model again, on purpose."
              : "Re-run the last execution with every question put to the model, to compare."}
          </p>
        </div>
        {playback.waiting && !playback.requeued && <ActionButton onClick={onAskAfresh}>Re-run, asking afresh</ActionButton>}
      </div>
    );
  }

  const text: Record<string, string> = {
    idle: "Pick a case and run it. With the crash on, the first attempt fails after the model has answered.",
    starting: "Scheduling a one-off manifest…",
    running: "The first attempt is running. Each answer is recorded before the train acts on it.",
    backoff: "The retry is on its way.",
    retrying: "The scheduler's retry is running the train again from the top.",
    requeue: "The re-run is asking the model every question again.",
  };
  return <div className="rounded-lg border border-border bg-bg-secondary p-4 text-sm text-text-secondary">{text[phase]}</div>;
}

const CELL_TONES = {
  model: "border-l-accent-bright",
  replay: "border-l-signal bg-signal/5",
  afresh: "border-l-afresh bg-afresh/5",
};
const PROVENANCE_TONES = { model: "text-accent-bright", replay: "font-semibold text-signal", afresh: "font-semibold text-afresh" };

/** Every answer the train needed: one row per question, one column per attempt. */
function Decisions({ scenario, playback }: { scenario: Scenario; playback: Playback | null }) {
  const questions = questionsOf(scenario);
  const attempts = playback?.attempts ?? [];

  let asked = 0;
  let replayed = 0;
  let savedMs = 0;
  attempts.forEach((a, index) =>
    questions.forEach((q) => {
      const s = questionStep(a, q.key);
      if (!s) return;
      if (!s.replayed) return void asked++;
      replayed++;
      // What the replayed answer would have cost: the same question's time in the attempt it came from.
      const source = attempts.find((x) => x.id === a.journal?.replayDecisionsOf) ?? attempts[index - 1];
      const original = source && questionStep(source, q.key);
      if (source && original) savedMs += answerMs(source, original) ?? 0;
    }),
  );

  return (
    <div className="min-w-0 overflow-hidden rounded-lg border border-border bg-bg-secondary">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-1 border-b border-border px-4 py-2.5">
        <span className="text-[11px] font-semibold uppercase tracking-wider text-text-muted">Decisions</span>
        <span className="ml-auto flex flex-wrap gap-x-4 text-xs text-text-muted">
          <span>
            <b className="mr-1 text-sm text-accent-bright">{asked}</b>model call{asked === 1 ? "" : "s"}
          </span>
          <span>
            <b className="mr-1 text-sm text-signal">{replayed}</b>replayed
            {savedMs > 0 ? `, ${seconds(savedMs)} of model time not spent again` : ""}
          </span>
        </span>
      </div>
      {attempts.length === 0 ? (
        <p className="px-4 py-4 text-sm text-text-muted">
          Each answer the train asks for lands here, with where it came from. Run a case with a crash to watch a retry reuse
          them.
        </p>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full border-collapse text-left text-sm">
            <thead>
              <tr className="border-b border-border">
                <th scope="col" className="px-4 py-2 text-xs font-semibold text-text-primary">
                  Question
                </th>
                {attempts.map((a) => (
                  <th key={a.id} scope="col" className="whitespace-nowrap px-4 py-2 text-xs font-semibold text-text-primary">
                    {labelOf(playback!, a)}
                    <span className="block font-normal text-text-muted">execution {a.id}</span>
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {questions.map((q) => (
                <tr key={q.key} className="border-b border-border last:border-b-0">
                  <th scope="row" className="px-4 py-2.5 align-top font-normal">
                    <code className="font-mono text-xs text-text-primary">{q.key}</code>
                    <span className="block text-xs text-text-muted">{q.label}</span>
                  </th>
                  {attempts.map((a) => {
                    const s = questionStep(a, q.key);
                    if (!s) {
                      const ended = a.trainState !== "IN_PROGRESS" && a.trainState !== "PENDING";
                      return (
                        <td key={a.id} className="min-w-[150px] px-4 py-2.5 align-top text-xs text-text-muted">
                          {ended ? "not reached" : <span className="animate-pulse">waiting</span>}
                        </td>
                      );
                    }
                    const badge = badgeOf(playback!, a, s)!;
                    const route = routeStep(a, q.key);
                    const value = route?.answer ?? s.answer;
                    const score = s.kind !== "CHOICE" && s.answer && s.answer !== value ? s.answer : null;
                    const ms = answerMs(a, s);
                    const hash = a.journal?.decisions.find((d) => d.questionKey === q.key)?.stateHash;
                    return (
                      <td key={a.id} className={`min-w-[150px] border-l-[3px] px-4 py-2.5 align-top ${CELL_TONES[badge.tone]}`}>
                        <div className="flex items-baseline gap-2 font-semibold text-text-primary">
                          {value}
                          {score && (
                            <span className="font-mono text-[11px] font-normal text-text-muted">
                              {s.kind === "YES_NO" ? `p ${score}` : `score ${score}`}
                            </span>
                          )}
                        </div>
                        <div className={`text-xs ${PROVENANCE_TONES[badge.tone]}`}>{badge.text}</div>
                        <div className="mt-0.5 flex gap-2.5 font-mono text-[10.5px] text-text-muted">
                          {ms != null && <span>{seconds(ms)}</span>}
                          {hash && <span title={`state hash ${hash}`}>state {hash.replace(/^[^:]*:/, "").slice(0, 8)}</span>}
                        </div>
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

const STATION_TONES: Record<string, string> = {
  ahead: "border-border-hover bg-bg-primary",
  in_progress: "border-info bg-info animate-pulse",
  completed: "border-accent-bright bg-accent-bright",
  failed: "border-derail bg-derail",
  cancelled: "border-border-hover bg-border-hover",
};
const stateOf = (s: Step | undefined) => (s ? s.state.toLowerCase() : "ahead");

/** One line per attempt: the train's whole route, with the tracks that attempt took lit. */
function RouteMap({ scenario, playback }: { scenario: Scenario; playback: Playback | null }) {
  const attempts = playback?.attempts ?? [];
  const maxRetries = playback?.run?.maxRetries ?? 2;
  return (
    <div className="min-w-0 overflow-hidden rounded-lg border border-border bg-bg-secondary">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1 border-b border-border px-4 py-2.5">
        <span className="text-[11px] font-semibold uppercase tracking-wider text-text-muted">Route taken</span>
        <span className="ml-auto flex flex-wrap gap-x-3 text-[11px] text-text-muted">
          <Key tone="bg-accent-bright">ran</Key>
          <Key tone="bg-info">running</Key>
          <Key tone="bg-derail">crashed</Key>
          <Key tone="bg-signal rotate-45 rounded-[2px]">answer replayed</Key>
        </span>
      </div>
      <div className="overflow-x-auto pb-2">
        {attempts.length === 0 ? (
          <RouteLine scenario={scenario} playback={null} attempt={null} status={null} label="Route" />
        ) : (
          attempts.map((a, index) => {
            const manifestIndex = attempts.filter((x) => x.origin === "manifest").findIndex((x) => x.id === a.id);
            const retried = attempts.slice(index + 1).some((x) => x.origin === "manifest");
            const status =
              a.trainState === "COMPLETED"
                ? { text: manifestIndex > 0 ? "Recovered" : "Completed", tone: "text-accent-bright border-accent/40" }
                : a.trainState === "FAILED"
                  ? {
                      text:
                        a.origin === "manifest" && manifestIndex < maxRetries
                          ? retried
                            ? "Crashed, retried"
                            : "Crashed, retry scheduled"
                          : "Crashed",
                      tone: "text-derail border-derail/40",
                    }
                  : { text: "Running", tone: "text-info border-info/40" };
            return <RouteLine key={a.id} scenario={scenario} playback={playback} attempt={a} status={status} label={labelOf(playback!, a)} />;
          })
        )}
      </div>
    </div>
  );
}

function Key({ tone, children }: { tone: string; children: React.ReactNode }) {
  return (
    <span className="flex items-center gap-1.5">
      <span className={`inline-block h-2 w-2 rounded-full ${tone}`} />
      {children}
    </span>
  );
}

function RouteLine({
  scenario,
  playback,
  attempt,
  status,
  label,
}: {
  scenario: Scenario;
  playback: Playback | null;
  attempt: Attempt | null;
  status: { text: string; tone: string } | null;
  label: string;
}) {
  return (
    <div className={`border-b border-border px-4 pb-2 pt-3 last:border-b-0 ${attempt ? "" : "opacity-55"}`}>
      <div className="mb-2 flex items-center gap-2.5 text-sm">
        <strong className="font-semibold text-text-primary">{label}</strong>
        {status && <span className={`rounded-full border px-2.5 py-0.5 text-xs ${status.tone}`}>{status.text}</span>}
      </div>
      <ol className="flex min-w-max items-center">
        {ROUTES[scenario].map((stop, i) => (
          <li key={stop.kind === "junction" ? stop.name : stop.key} className="flex items-center">
            {i > 0 && <span className="mx-1 h-0.5 w-3.5 bg-border-hover" />}
            {stop.kind === "junction" ? (
              <Station name={stop.name} step={attempt ? junctionStep(attempt, stop.name) : undefined} />
            ) : (
              <Fork stop={stop} playback={playback} attempt={attempt} />
            )}
          </li>
        ))}
      </ol>
    </div>
  );
}

function Station({ name, step }: { name: string; step: Step | undefined }) {
  const state = stateOf(step);
  return (
    <span className="flex items-center gap-1.5 font-mono text-[11px]" title={step?.failureException}>
      <span className={`h-3 w-3 shrink-0 rounded-full border-2 ${STATION_TONES[state]}`} />
      <span className={state === "ahead" ? "text-text-muted" : state === "failed" ? "text-derail" : "text-text-primary"}>{name}</span>
    </span>
  );
}

const SWITCH_TONES = {
  ahead: "border-border-hover bg-bg-primary",
  model: "border-accent-bright bg-accent-muted",
  replay: "border-signal bg-signal/40",
  afresh: "border-afresh bg-afresh/30",
};
const TRACK_TONES: Record<string, string> = {
  completed: "border-accent/50 bg-bg-tertiary text-text-primary",
  in_progress: "border-info bg-bg-tertiary text-text-primary",
  failed: "border-derail bg-derail/10 text-derail",
  ahead: "border-border-hover bg-bg-tertiary text-text-primary",
  cancelled: "border-border-hover bg-bg-tertiary text-text-primary",
};

function Fork({ stop, playback, attempt }: { stop: Extract<Stop, { kind: "question" }>; playback: Playback | null; attempt: Attempt | null }) {
  const question = attempt ? questionStep(attempt, stop.key) : undefined;
  const route = attempt ? routeStep(attempt, stop.key) : undefined;
  const tone = attempt && question && playback ? badgeOf(playback, attempt, question)!.tone : "ahead";
  return (
    <span className="flex items-center gap-1.5">
      <span className={`mx-1 h-5 w-5 shrink-0 rotate-45 rounded-[4px] border-2 ${SWITCH_TONES[tone]}`} title={stop.label} />
      <span className="flex flex-col gap-1">
        <span className="pl-2.5 font-mono text-[11px] text-text-muted">{stop.key}?</span>
        <ul className="flex flex-col gap-0.5 border-l-2 border-border-hover pl-2">
          {stop.tracks.map((t) => {
            const taken = route?.answer === t.answer;
            const s = attempt && taken ? junctionStep(attempt, t.junction) : undefined;
            return (
              <li
                key={t.answer}
                title={s?.failureException}
                className={`rounded border px-1.5 py-0.5 text-[11px] leading-tight ${
                  taken ? TRACK_TONES[stateOf(s)] : `border-transparent text-text-muted ${route ? "line-through opacity-50" : ""}`
                }`}
              >
                <span className={`block font-semibold ${taken && s?.state === "COMPLETED" ? "text-accent-bright" : ""}`}>{t.answer}</span>
                {taken && <span className="block font-mono text-[10.5px]">{t.junction}</span>}
              </li>
            );
          })}
        </ul>
      </span>
    </span>
  );
}

// The words that declare a decision and its tracks, picked out in the code.
const TOKEN =
  /(\.(?:Switch|Scale|Gate|When|AtLeast|Yes|No|Unsure)\b)|(\[Trax[A-Za-z]*)|("[^"]*")|(\b(?:public|class|protected|override)\b)/g;

function highlight(line: string) {
  const parts: React.ReactNode[] = [];
  let last = 0;
  for (const m of line.matchAll(TOKEN)) {
    if (m.index > last) parts.push(line.slice(last, m.index));
    const tone = m[1] ? "text-signal" : m[2] ? "text-info" : m[3] ? "text-accent-bright" : "text-text-muted";
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

function CodePanel({ source, step }: { source: { file: string; code: string }; step: Step | null }) {
  const lines = useMemo(() => source.code.split("\n"), [source.code]);
  // Only the chain, from Junctions() to Resolve(), dedented. Lines never wrap: useFitCode sizes the font so the
  // longest fits, and below its smallest size the panel scrolls sideways. The line numbers stay the file's.
  const [first, last, indent] = useMemo(() => {
    const at = lines.findIndex((l) => l.includes("Junctions()"));
    const end = lines.findIndex((l, i) => i > at && l.includes(".Resolve()"));
    const shown = at < 0 || end < 0 ? [0, lines.length] : [at, end + 1];
    const indent = Math.min(...lines.slice(shown[0], shown[1]).filter((l) => l.trim()).map((l) => l.search(/\S/)));
    return [shown[0], shown[1], indent];
  }, [lines]);
  const pane = useRef<HTMLPreElement>(null);
  useFitCode(pane, source.file, 13);
  const highlighted = step ? lineOf(source.code, step) : -1;
  const tone =
    step?.state === "FAILED"
      ? "border-derail bg-derail/15"
      : step?.state === "IN_PROGRESS"
        ? "border-info bg-info/15"
        : "border-accent-bright bg-accent/15";

  return (
    <div className="flex min-w-0 flex-col">
      <div className="border-b border-border px-4 py-1.5 font-mono text-xs text-text-muted">{source.file}</div>
      <pre ref={pane} className="overflow-x-auto py-3 font-mono text-[12px] leading-[1.7] text-text-secondary">
        {lines.slice(first, last).map((line, offset) => {
          const i = first + offset;
          return (
            <div
              key={i}
              className={`flex w-max min-w-full border-l-2 pr-4 transition-colors duration-200 ${i === highlighted ? tone : "border-transparent"}`}
            >
              <span className="mr-4 w-8 shrink-0 select-none text-right text-text-muted/60">{i + 1}</span>
              <span className="whitespace-pre">{highlight(line.slice(indent))}</span>
            </div>
          );
        })}
      </pre>
    </div>
  );
}

const ROW_TONES: Record<string, string> = {
  info: "text-text-primary",
  model: "text-accent-bright",
  replay: "text-signal",
  error: "text-derail",
  success: "text-accent-bright",
  system: "text-text-secondary",
};

/** Every junction event the recording received, in arrival order, beside what the page itself did. */
function EventFeed({ playback }: { playback: Playback | null }) {
  const rows = useMemo(() => {
    if (!playback) return [];
    const all: { key: string; t: number; who: string; what: string; detail: string; tone: string }[] = playback.lines.map(
      (l: Line) => ({ key: l.key, t: l.t, who: "page", what: "", detail: l.text, tone: l.tone }),
    );
    for (const a of playback.attempts)
      for (const s of Object.values(a.steps)) {
        const name = s.kind === "JUNCTION" ? s.name : (s.questionKey ?? s.name);
        const detail =
          s.kind === "JUNCTION"
            ? s.state === "FAILED"
              ? (s.failureException ?? "failed")
              : s.state === "COMPLETED"
                ? `${s.durationMs ?? 0} ms`
                : "running"
            : s.kind === "ROUTE"
              ? `track ${s.answer}`
              : `answer ${s.answer}${s.confidence != null ? `, confidence ${s.confidence.toFixed(2)}` : ""}${s.replayed ? ", replayed" : ""}`;
        all.push({
          key: `${a.id}:${s.position}:${s.state}`,
          t: s.end ?? s.start ?? 0,
          who: labelOf(playback, a),
          what: `#${s.position} ${s.kind} ${name}`,
          detail,
          tone: s.state === "FAILED" ? "error" : s.replayed ? "replay" : s.kind === "JUNCTION" ? "info" : "model",
        });
      }
    return all.sort((x, y) => x.t - y.t);
  }, [playback]);

  const box = useRef<HTMLDivElement>(null);
  // Scroll the feed itself, never the page.
  useLayoutEffect(() => {
    if (box.current) box.current.scrollTop = box.current.scrollHeight;
  }, [rows.length]);

  return (
    <div ref={box} className="h-[300px] overflow-y-auto px-3 py-2 font-mono text-[11.5px] leading-relaxed">
      {rows.length === 0 && <p className="px-1 py-2 font-sans text-sm text-text-muted">The onJunctionEvent subscription&apos;s events appear here as they arrive.</p>}
      {rows.map((r) => (
        <div key={r.key} className="grid grid-cols-[3.5em_6em_minmax(0,1fr)] gap-x-3 border-b border-border/60 px-1 py-0.5">
          <span className="text-text-muted/70">{(r.t / 1000).toFixed(1)}s</span>
          <span className="text-text-muted">{r.who}</span>
          <span className={ROW_TONES[r.tone]}>
            {r.what && <span className="mr-2">{r.what}</span>}
            <span className={r.what ? "text-text-muted" : ""}>{r.detail}</span>
          </span>
        </div>
      ))}
    </div>
  );
}
