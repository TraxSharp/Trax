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
  RESUME,
  RUN_GRAPH,
  START_RUN,
  TRIGGER_ASK_AFRESH,
  WORK_QUEUE_ENTRY,
} from "./graphql";
import { LatestReads, mergeSteps as merged } from "./runReads";
import {
  RESUME_AT,
  treeOf,
  type Attempt,
  type FlatGraphNode,
  type Fork,
  type Journal,
  type Phase,
  type RunGraph,
  type RunInfo,
  type Scenario,
  type Step,
} from "./types";
import { isQuestion, resumableAttempt } from "./workflowView";

const POLL_MS = 500;

interface ExecutionRow {
  id: number;
  trainState: string;
  startTime: string;
  endTime: string | null;
  failureJunction: string | null;
  failureReason: string | null;
}

const ENDED = new Set(["COMPLETED", "FAILED", "CANCELLED"]);

/**
 * Follows one demo run: its manifest's attempts (found by polling operations.executions), each attempt's steps
 * (onJunctionEvent, merged with operations.junctionRuns by position), its run graph (operations.runGraph) while it
 * runs, and its decision journal, read as soon as a question's step arrives and once more when the attempt ends.
 */
export function useRecoveryRun() {
  const client = useApolloClient();
  const [run, setRun] = useState<RunInfo | null>(null);
  const [attempts, setAttempts] = useState<Attempt[]>([]);
  const [starting, setStarting] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [forkTaken, setForkTaken] = useState<Fork>("none");

  const subscriptions = useRef(new Map<number, { unsubscribe(): void }>());
  const journaled = useRef(new Set<number>());
  // Attempts whose run graph was read after they ended: nothing on it changes any more.
  const graphed = useRef(new Set<number>());
  // The attempts in the order they were found, kept outside React state so a new one is labelled at once.
  const order = useRef<{ id: number; origin: Attempt["origin"] }[]>([]);
  // The questions whose answers were read from the journal, by attempt and question.
  const askedFor = useRef(new Set<string>());
  const forkRef = useRef<Fork>("none");
  // Bumped by every reset, so an answer to a read the reset outdated is dropped.
  const generation = useRef(0);

  const readJournal = useCallback(
    async (attemptId: number) => {
      const at = generation.current;
      const { data } = await client.query({ query: DECISION_JOURNAL, variables: { metadataId: attemptId } });
      if (at !== generation.current) return;
      const journal = data.discover.decisionJournal as Journal;
      setAttempts((all) => all.map((x) => (x.id === attemptId ? { ...x, journal } : x)));
    },
    [client],
  );

  const mergeSteps = useCallback(
    (attemptId: number, incoming: Step[]) => {
      // A question's step arrives once the answer is recorded: read the journal for its words and the answer given.
      for (const step of incoming.filter(isQuestion)) {
        const key = `${attemptId}:${step.position}`;
        if (askedFor.current.has(key)) continue;
        askedFor.current.add(key);
        void readJournal(attemptId).catch(() => askedFor.current.delete(key));
      }
      setAttempts((all) => all.map((a) => (a.id === attemptId ? { ...a, steps: merged(a.steps, incoming) } : a)));
    },
    [readJournal],
  );

  const readStored = useCallback(
    async (attemptId: number) => {
      const at = generation.current;
      const { data } = await client.query({ query: JUNCTION_RUNS, variables: { metadataId: attemptId } });
      if (at === generation.current) mergeSteps(attemptId, data.operations.junctionRuns as Step[]);
    },
    [client, mergeSteps],
  );

  // Each attempt's graph reads are numbered, so a read that answers after a later one started (a slow poll landing
  // after the final read) is dropped rather than drawn over a newer graph.
  const graphReads = useRef(new LatestReads());
  const readGraph = useCallback(
    async (attemptId: number): Promise<void> => {
      const read = graphReads.current.begin(attemptId);
      const at = generation.current;
      const { data } = await client.query({ query: RUN_GRAPH, variables: { metadataId: attemptId } });
      if (!graphReads.current.isLatest(attemptId, read) || at !== generation.current) return;
      const flat = data.operations.runGraph as { hasGraph: boolean; allNodes: FlatGraphNode[] } | null;
      const graph: RunGraph | null = flat && { hasGraph: flat.hasGraph, nodes: treeOf(flat.allNodes) };
      setAttempts((all) => all.map((a) => (a.id === attemptId ? { ...a, graph } : a)));
    },
    [client],
  );

  const follow = useCallback(
    (row: ExecutionRow, origin: Attempt["origin"]) => {
      if (subscriptions.current.has(row.id)) return;
      const startedBy: Attempt["startedBy"] =
        origin === "requeue"
          ? "requeue"
          : origin === "resume"
            ? "resume"
            : forkRef.current === "askAfresh" && order.current.length > 0
              ? "askAfresh"
              : null;
      order.current.push({ id: row.id, origin });
      setAttempts((all) =>
        all.some((a) => a.id === row.id) ? all : [...all, { ...row, origin, startedBy, steps: {}, journal: null, graph: null }],
      );

      const sub = client.subscribe({ query: ON_JUNCTION_EVENT, variables: { metadataId: row.id } }).subscribe({
        next: ({ data }) => {
          if (data?.onJunctionEvent) mergeSteps(row.id, [data.onJunctionEvent.junction as Step]);
        },
      });
      subscriptions.current.set(row.id, sub);
      // Subscribe first, then read what was stored before the subscription started.
      void readStored(row.id).catch(() => undefined);
    },
    [client, mergeSteps, readStored],
  );

  const updateRow = useCallback((row: ExecutionRow) => {
    setAttempts((all) => all.map((a) => (a.id === row.id ? { ...a, ...row } : a)));
  }, []);

  // Poll the manifest's executions for new attempts and each attempt's state.
  useEffect(() => {
    if (!run) return;
    let cancelled = false;
    // One tick at a time, and one graph read per attempt at a time: a slow host makes the polls wait rather than
    // pile up.
    let ticking = false;
    const graphing = new Set<number>();
    const pollGraph = (id: number) => {
      if (graphing.has(id)) return;
      graphing.add(id);
      readGraph(id)
        .catch(() => undefined)
        .finally(() => graphing.delete(id));
    };
    const tick = async () => {
      if (ticking) return;
      ticking = true;
      try {
        const { data } = await client.query({ query: EXECUTIONS, variables: { manifestId: run.manifestId } });
        if (cancelled) return;
        const rows = data.operations.executions.items as ExecutionRow[];
        for (const row of rows) {
          follow(row, "manifest");
          updateRow(row);
        }
        for (const id of [...subscriptions.current.keys()]) {
          if (!graphed.current.has(id)) pollGraph(id);
          if (!rows.some((r) => r.id === id)) {
            const one = await client.query({ query: EXECUTION, variables: { id } });
            if (cancelled) return;
            if (one.data.operations.execution) updateRow(one.data.operations.execution as ExecutionRow);
          }
        }
      } catch {
        // The host may be restarting; the next tick tries again.
      } finally {
        ticking = false;
      }
    };
    void tick();
    const timer = setInterval(tick, POLL_MS);
    return () => {
      cancelled = true;
      clearInterval(timer);
    };
  }, [client, run, follow, updateRow, readGraph]);

  // Once an attempt ends: read its steps, its graph and its decision journal one last time.
  useEffect(() => {
    for (const a of attempts) {
      if (!ENDED.has(a.trainState) || journaled.current.has(a.id)) continue;
      journaled.current.add(a.id);
      void (async () => {
        await readStored(a.id);
        graphed.current.add(a.id);
        await readGraph(a.id);
        await readJournal(a.id);
      })().catch(() => undefined);
    }
  }, [attempts, readStored, readGraph, readJournal]);

  const reset = useCallback(() => {
    generation.current++;
    subscriptions.current.forEach((s) => s.unsubscribe());
    subscriptions.current.clear();
    journaled.current.clear();
    graphed.current.clear();
    askedFor.current.clear();
    order.current = [];
    forkRef.current = "none";
    setAttempts([]);
    setRun(null);
    setProblem(null);
    setForkTaken("none");
  }, []);

  useEffect(() => {
    const live = subscriptions.current;
    return () => live.forEach((s) => s.unsubscribe());
  }, []);

  const start = useCallback(
    async (scenario: Scenario, crashOnce: boolean, choice: Record<string, unknown>) => {
      reset();
      setStarting(true);
      try {
        const { data } = await client.mutate({ mutation: START_RUN, variables: { input: { scenario, crashOnce, ...choice } } });
        setRun({ ...data.dispatch.startRun.output, scenario });
      } catch (error) {
        setProblem(`Could not start: ${(error as Error).message}`);
      } finally {
        setStarting(false);
      }
    },
    [client, reset],
  );

  const phase: Phase = useMemo(() => {
    if (starting) return "starting";
    if (!run) return "idle";
    const requeued = [...attempts].reverse().find((a) => a.origin === "requeue" || a.origin === "resume");
    if (requeued) return requeued.trainState === "IN_PROGRESS" || requeued.trainState === "PENDING" ? "requeue" : "done";
    const manifestRuns = attempts.filter((a) => a.origin === "manifest");
    const last = manifestRuns[manifestRuns.length - 1];
    if (!last) return "starting";
    if (last.trainState === "COMPLETED") return "done";
    if (last.trainState === "FAILED") return manifestRuns.length > (run.maxRetries ?? 2) ? "dead" : "backoff";
    return manifestRuns.length > 1 ? "retrying" : "running";
  }, [attempts, run, starting]);

  /** Waits for a requeued or resumed work queue entry to be dispatched, then follows its run. */
  const followEntry = useCallback(
    async (entryId: number, origin: "requeue" | "resume") => {
      for (let i = 0; i < 60; i++) {
        const entry = await client.query({ query: WORK_QUEUE_ENTRY, variables: { id: entryId } });
        const metadataId = entry.data.operations.workQueue.workQueue?.metadataId;
        if (metadataId) {
          const one = await client.query({ query: EXECUTION, variables: { id: metadataId } });
          follow(one.data.operations.execution as ExecutionRow, origin);
          return;
        }
        await new Promise((r) => setTimeout(r, 250));
      }
    },
    [client, follow],
  );

  const askAfresh = useCallback(async () => {
    if (!run) return;
    try {
      if (phase === "backoff") {
        forkRef.current = "askAfresh";
        setForkTaken("askAfresh");
        await client.mutate({ mutation: TRIGGER_ASK_AFRESH, variables: { externalId: run.manifestExternalId } });
        return;
      }
      const last = attempts[attempts.length - 1];
      if (!last) return;
      const { data } = await client.mutate({ mutation: REQUEUE, variables: { id: last.id, askAfresh: true } });
      const result = data.operations.requeueExecution;
      if (!result.success) setProblem(result.message);
      else if (result.id != null) await followEntry(result.id, "requeue");
    } catch (error) {
      setProblem((error as Error).message);
    }
  }, [attempts, client, followEntry, phase, run]);

  const resumable = useMemo(() => resumableAttempt(attempts, phase), [attempts, phase]);

  const resumeFromNotify = useCallback(async () => {
    if (!resumable) return;
    try {
      const { data } = await client.mutate({ mutation: RESUME, variables: { id: resumable.id, from: RESUME_AT } });
      const result = data.operations.resumeExecution;
      if (!result.success) setProblem(result.message);
      else if (result.id != null) await followEntry(result.id, "resume");
    } catch (error) {
      setProblem((error as Error).message);
    }
  }, [client, followEntry, resumable]);

  const changeData = useCallback(async () => {
    if (!run) return;
    forkRef.current = "changeData";
    setForkTaken("changeData");
    try {
      await client.mutate({ mutation: CHANGE_CASE_DATA, variables: { runId: run.runId } });
    } catch (error) {
      setProblem((error as Error).message);
    }
  }, [client, run]);

  return { run, attempts, phase, forkTaken, resumable, problem, start, reset, askAfresh, resumeFromNotify, changeData };
}
