import { useApolloClient } from "@apollo/client";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  CHANGE_CASE_DATA,
  DECISION_JOURNAL,
  EXECUTION,
  EXECUTIONS,
  JUNCTION_RUNS,
  ON_JUNCTION_EVENT,
  REQUEUE,
  START_RUN,
  TRIGGER_ASK_AFRESH,
  WORK_QUEUE_ENTRY,
} from "./graphql";
import { shownAnswer, type Attempt, type ConsoleLine, type Fork, type Journal, type Phase, type RunInfo, type Scenario, type Step, type Tone } from "./types";

const POLL_MS = 500;
const RANK = { IN_PROGRESS: 0, COMPLETED: 1, FAILED: 1, CANCELLED: 1 } as const;

interface ExecutionRow {
  id: number;
  trainState: string;
  startTime: string;
  endTime: string | null;
  failureJunction: string | null;
  failureReason: string | null;
}

/**
 * Follows one demo run: its manifest's attempts (found by polling operations.executions), each
 * attempt's steps (onJunctionEvent, merged with operations.junctionRuns by position), and the
 * decision journal once an attempt ends. Narrates what it sees into console lines.
 */
export function useRecoveryRun() {
  const client = useApolloClient();
  const [run, setRun] = useState<RunInfo | null>(null);
  const [attempts, setAttempts] = useState<Attempt[]>([]);
  const [lines, setLines] = useState<ConsoleLine[]>([]);
  const [starting, setStarting] = useState(false);
  const [forkTaken, setForkTaken] = useState<Fork>("none");

  // When Run was pressed: console times and the timeline count from here.
  const base = useRef(Date.now());
  const narrated = useRef(new Set<string>());
  const subscriptions = useRef(new Map<number, { unsubscribe(): void }>());
  const journaled = useRef(new Set<number>());
  // The attempts in the order they were found, kept outside React state so narration can label them at once.
  const order = useRef<{ id: number; origin: Attempt["origin"] }[]>([]);
  const stepRank = useRef(new Map<string, number>());
  const runRef = useRef<RunInfo | null>(null);
  const forkRef = useRef<Fork>("none");

  const say = useCallback((key: string, tone: Tone, text: string) => {
    if (narrated.current.has(key)) return;
    narrated.current.add(key);
    setLines((all) => [...all, { key, t: Date.now() - base.current, tone, text }]);
  }, []);

  const labelOf = useCallback((attempt: Pick<Attempt, "id" | "origin">) => {
    if (attempt.origin === "requeue") return "[requeue]";
    const index = order.current.filter((a) => a.origin === "manifest").findIndex((a) => a.id === attempt.id);
    return `[attempt ${index + 1}]`;
  }, []);

  const narrate = useCallback(
    (attemptId: number, step: Step) => {
      const known = order.current.find((a) => a.id === attemptId);
      if (!known) return;
      const label = labelOf(known);
      const key = `${attemptId}:${step.position}:${step.state}`;
      const name = step.nameWithheld ? "(withheld)" : step.name;
      if (step.kind === "JUNCTION") {
        if (step.state === "IN_PROGRESS") say(key, "info", `${label} # JUNCTION ${name} is running...`);
        else if (step.state === "COMPLETED")
          say(key, "info", `${label} # JUNCTION ${name} completed in ${Math.round(step.durationMs ?? 0)} ms`);
        else
          say(
            key,
            "error",
            `${label} # JUNCTION ${name} failed with ${step.failureException ?? "an exception"}. The run has crashed: Trax records the failure and the manifest will retry it.`,
          );
      } else if (step.kind === "ROUTE") {
        say(key, "info", `${label} # ROUTE took the ${step.answer} track of ${step.questionKey ?? name}`);
      } else if (step.replayed) {
        say(
          key,
          "replay",
          `${label} # MODEL not asked: ${step.questionKey} = ${shownAnswer(step.answer)} replayed from the failed attempt, because the state hashes the same.`,
        );
      } else {
        const confidence = step.confidence != null ? ` (confidence ${step.confidence.toFixed(2)})` : "";
        say(
          key,
          "model",
          `${label} # MODEL asked: ${step.questionKey} = ${shownAnswer(step.answer)}${confidence}. Trax recorded the answer before acting on it.`,
        );
      }
    },
    [labelOf, say],
  );

  const mergeSteps = useCallback(
    (attemptId: number, incoming: Step[]) => {
      // Narrate each step state once, and never a state older than one already seen.
      for (const step of [...incoming].sort((a, b) => a.position - b.position)) {
        const key = `${attemptId}:${step.position}`;
        const known = stepRank.current.get(key);
        if (known != null && known >= RANK[step.state]) continue;
        stepRank.current.set(key, RANK[step.state]);
        narrate(attemptId, step);
      }
      setAttempts((all) =>
        all.map((a) => {
          if (a.id !== attemptId) return a;
          const steps = { ...a.steps };
          for (const step of incoming) {
            const known = steps[step.position];
            if (!known || RANK[known.state] < RANK[step.state]) steps[step.position] = step;
          }
          return { ...a, steps };
        }),
      );
    },
    [narrate],
  );

  const readStored = useCallback(
    async (attemptId: number) => {
      const { data } = await client.query({ query: JUNCTION_RUNS, variables: { metadataId: attemptId } });
      mergeSteps(attemptId, data.operations.junctionRuns as Step[]);
    },
    [client, mergeSteps],
  );

  const follow = useCallback(
    (row: ExecutionRow, origin: Attempt["origin"]) => {
      if (subscriptions.current.has(row.id)) return;
      const startedBy: Attempt["startedBy"] =
        origin === "requeue" ? "requeue" : forkRef.current === "askAfresh" && order.current.length > 0 ? "askAfresh" : null;
      order.current.push({ id: row.id, origin });
      setAttempts((all) =>
        all.some((a) => a.id === row.id) ? all : [...all, { ...row, origin, startedBy, steps: {}, journal: null }],
      );

      const label = labelOf({ id: row.id, origin });
      const index = order.current.filter((a) => a.origin === "manifest").length - 1;
      const text =
        startedBy === "requeue"
          ? `RUN execution ${row.id} started by the requeue`
          : startedBy === "askAfresh"
            ? `RUN execution ${row.id} started by the trigger, without waiting out the backoff`
            : index === 0
              ? `RUN execution ${row.id} started`
              : `RETRY ${index}/${runRef.current?.maxRetries ?? 2}: the manifest's retry started as execution ${row.id}`;
      say(`${row.id}:start`, "system", `${label} # ${text}`);

      const sub = client
        .subscribe({ query: ON_JUNCTION_EVENT, variables: { metadataId: row.id } })
        .subscribe({
          next: ({ data }) => {
            if (data?.onJunctionEvent) mergeSteps(row.id, [data.onJunctionEvent.junction as Step]);
          },
        });
      subscriptions.current.set(row.id, sub);
      // Subscribe first, then read what was stored before the subscription started.
      void readStored(row.id);
    },
    [client, labelOf, mergeSteps, readStored, say],
  );

  const updateRow = useCallback(
    (row: ExecutionRow) => {
      const known = order.current.find((a) => a.id === row.id);
      if (known?.origin === "manifest" && row.trainState === "FAILED") {
        const index = order.current.filter((a) => a.origin === "manifest").findIndex((a) => a.id === row.id);
        const max = runRef.current?.maxRetries ?? 2;
        say(
          `${row.id}:end`,
          "system",
          index < max
            ? `${labelOf(known)} # FAILED. The scheduler retries after its backoff (a few seconds here), naming execution ${row.id} as the run to replay.`
            : `${labelOf(known)} # FAILED. Every retry is spent, so the manifest is dead-lettered.`,
        );
      }
      setAttempts((all) => all.map((a) => (a.id === row.id ? { ...a, ...row } : a)));
    },
    [labelOf, say],
  );

  // Poll the manifest's executions for new attempts and each attempt's state.
  useEffect(() => {
    if (!run) return;
    let cancelled = false;
    const tick = async () => {
      try {
        const { data } = await client.query({ query: EXECUTIONS, variables: { manifestId: run.manifestId } });
        if (cancelled) return;
        const rows = data.operations.executions.items as ExecutionRow[];
        for (const row of rows) {
          follow(row, "manifest");
          updateRow(row);
        }
        for (const id of [...subscriptions.current.keys()]) {
          if (!rows.some((r) => r.id === id)) {
            const one = await client.query({ query: EXECUTION, variables: { id } });
            if (one.data.operations.execution) updateRow(one.data.operations.execution as ExecutionRow);
          }
        }
      } catch {
        // The host may be restarting; the next tick tries again.
      }
    };
    void tick();
    const timer = setInterval(tick, POLL_MS);
    return () => {
      cancelled = true;
      clearInterval(timer);
    };
  }, [client, run, follow, updateRow]);

  // Once an attempt ends: read its steps one last time and its decision journal.
  useEffect(() => {
    for (const a of attempts) {
      if (a.trainState !== "COMPLETED" && a.trainState !== "FAILED" && a.trainState !== "CANCELLED") continue;
      if (journaled.current.has(a.id)) continue;
      journaled.current.add(a.id);
      void (async () => {
        await readStored(a.id);
        const { data } = await client.query({ query: DECISION_JOURNAL, variables: { metadataId: a.id } });
        const journal = data.discover.decisionJournal as Journal;
        setAttempts((all) => all.map((x) => (x.id === a.id ? { ...x, journal } : x)));
        if (a.trainState === "COMPLETED") say(`${a.id}:end`, "success", `${labelOf(a)} # COMPLETED. ${describeJournal(journal)}`);
      })();
    }
  }, [attempts, client, labelOf, readStored, say]);

  const reset = useCallback(() => {
    subscriptions.current.forEach((s) => s.unsubscribe());
    subscriptions.current.clear();
    narrated.current.clear();
    journaled.current.clear();
    stepRank.current.clear();
    order.current = [];
    runRef.current = null;
    forkRef.current = "none";
    setAttempts([]);
    setLines([]);
    setRun(null);
    setForkTaken("none");
  }, []);

  useEffect(() => {
    const live = subscriptions.current;
    return () => live.forEach((s) => s.unsubscribe());
  }, []);

  const start = useCallback(
    async (scenario: Scenario, crashOnce: boolean, orderId: string, topic: string) => {
      reset();
      base.current = Date.now();
      setStarting(true);
      try {
        const { data } = await client.mutate({
          mutation: START_RUN,
          variables: {
            input: scenario === "RESEARCH" ? { scenario, crashOnce, topic } : { scenario, crashOnce, orderId },
          },
        });
        const output = data.dispatch.startRun.output;
        const info: RunInfo = { ...output, scenario };
        runRef.current = info;
        setRun(info);
        const crash =
          output.armedCrash === "REPORT"
            ? ", with a crash armed in the report step of the first attempt"
            : output.armedCrash === "REFUND_TRACK"
              ? ", with a crash armed in the step after the approval, on the first attempt"
              : "";
        say("start", "system", `# Scheduled a one-off manifest (MaxRetries ${output.maxRetries})${crash}`);
      } catch (error) {
        say(`start-error-${Date.now()}`, "error", `# Could not start: ${(error as Error).message}`);
      } finally {
        setStarting(false);
      }
    },
    [client, reset, say],
  );

  const phase: Phase = useMemo(() => {
    if (starting) return "starting";
    if (!run) return "idle";
    const requeued = [...attempts].reverse().find((a) => a.origin === "requeue");
    if (requeued) return requeued.trainState === "IN_PROGRESS" || requeued.trainState === "PENDING" ? "requeue" : "done";
    const manifestRuns = attempts.filter((a) => a.origin === "manifest");
    const last = manifestRuns[manifestRuns.length - 1];
    if (!last) return "starting";
    if (last.trainState === "COMPLETED") return "done";
    if (last.trainState === "FAILED") return manifestRuns.length > (run.maxRetries ?? 2) ? "dead" : "backoff";
    return manifestRuns.length > 1 ? "retrying" : "running";
  }, [attempts, run, starting]);

  const askAfresh = useCallback(async () => {
    if (!run) return;
    if (phase === "backoff") {
      forkRef.current = "askAfresh";
      setForkTaken("askAfresh");
      const { data } = await client.mutate({ mutation: TRIGGER_ASK_AFRESH, variables: { externalId: run.manifestExternalId } });
      say("action:askAfresh", "system", `# ASK AFRESH: triggerManifest(askAfresh: true) says "${data.operations.triggerManifest.message}"`);
      return;
    }
    const last = attempts[attempts.length - 1];
    if (!last) return;
    const { data } = await client.mutate({ mutation: REQUEUE, variables: { id: last.id, askAfresh: true } });
    const result = data.operations.requeueExecution;
    say(`action:requeue:${last.id}`, "system", `# ASK AFRESH: requeueExecution(${last.id}, askAfresh: true) says "${result.message}"`);
    if (!result.success || result.id == null) return;
    for (let i = 0; i < 60; i++) {
      const entry = await client.query({ query: WORK_QUEUE_ENTRY, variables: { id: result.id } });
      const metadataId = entry.data.operations.workQueue.workQueue?.metadataId;
      if (metadataId) {
        const one = await client.query({ query: EXECUTION, variables: { id: metadataId } });
        follow(one.data.operations.execution as ExecutionRow, "requeue");
        return;
      }
      await new Promise((r) => setTimeout(r, 250));
    }
  }, [attempts, client, follow, phase, run, say]);

  const changeData = useCallback(async () => {
    if (!run) return;
    forkRef.current = "changeData";
    setForkTaken("changeData");
    const { data } = await client.mutate({ mutation: CHANGE_CASE_DATA, variables: { runId: run.runId } });
    say("action:changeData", "system", `# DATA CHANGED during the backoff: ${data.dispatch.changeCaseData.output.change}`);
  }, [client, run, say]);

  /** Milliseconds since Run was pressed, for a timestamp the host sent. */
  const sinceStart = useCallback((iso: string | null | undefined) => (iso ? Date.parse(iso) - base.current : undefined), []);

  return { run, attempts, lines, phase, forkTaken, start, reset, askAfresh, changeData, labelOf, sinceStart };
}

function describeJournal(journal: Journal): string {
  const replayed = journal.decisions.filter((d) => d.replayed).length;
  const refused = journal.decisions.filter((d) => d.replayRefused).length;
  const asked = journal.decisions.length - replayed;
  const parts = [`${journal.decisions.length} decision(s) recorded`];
  if (replayed) parts.push(`${replayed} replayed without calling the model`);
  if (asked) parts.push(`${asked} asked of the model`);
  if (refused) parts.push(`${refused} replay(s) refused because the state changed`);
  return parts.join(", ") + ".";
}
