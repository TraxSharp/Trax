import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { changeCaseData, decisionJournal, followRunProgress, readRunProgress, startRun, type RunProgress } from "./trax/index.g";
import { mergeSteps as merged } from "./runReads";
import type { Attempt, Fork, Journal, Phase, RunInfo, Scenario, Step } from "./types";
import { isQuestion } from "./workflowView";

const ENDED = new Set(["COMPLETED", "FAILED", "CANCELLED"]);

/**
 * Follows one demo run as the caller who started it: the manifest's first run through runProgress, then each retry
 * the run before it names (retriedAs), each attempt's steps as they happen, and its decision journal, read as soon as
 * a question's step arrives and once more when the attempt ends. Requeue, resume and asking afresh are an operator's,
 * in the dashboard.
 */
export function useRecoveryRun() {
  const [run, setRun] = useState<RunInfo | null>(null);
  const [attempts, setAttempts] = useState<Attempt[]>([]);
  const [starting, setStarting] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [forkTaken, setForkTaken] = useState<Fork>("none");

  const subscriptions = useRef(new Map<string, () => void>());
  const journaled = useRef(new Set<string>());
  // The questions whose answers were read from the journal, by attempt and question.
  const askedFor = useRef(new Set<string>());
  // Bumped by every reset, so an answer to a read the reset outdated is dropped.
  const generation = useRef(0);
  // The retry a run names is followed by the same function, through this ref.
  const followNext = useRef<(attemptId: string) => void>(() => undefined);

  const readJournal = useCallback(async (attemptId: string) => {
    const at = generation.current;
    const journal = (await decisionJournal({ runId: attemptId })) as unknown as Journal | null;
    if (at !== generation.current || !journal) return;
    setAttempts((all) => all.map((x) => (x.id === attemptId ? { ...x, journal } : x)));
  }, []);

  const mergeSteps = useCallback(
    (attemptId: string, incoming: Step[]) => {
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

  // The attempt's state as runProgress reports it, with where it failed.
  const updateState = useCallback((attemptId: string, state: RunProgress["state"], failure: string | null | undefined) => {
    if (!state) return;
    setAttempts((all) =>
      all.map((a) => {
        if (a.id !== attemptId) return a;
        const failed = Object.values(a.steps).find((s) => s.state === "FAILED");
        return {
          ...a,
          trainState: state,
          endTime: ENDED.has(state) ? (a.endTime ?? new Date().toISOString()) : null,
          failureJunction: state === "FAILED" ? (failed?.name ?? a.failureJunction) : a.failureJunction,
          failureReason: failure ?? a.failureReason,
        };
      }),
    );
  }, []);

  const follow = useCallback(
    (attemptId: string, origin: Attempt["origin"]) => {
      if (subscriptions.current.has(attemptId)) return;
      const at = generation.current;
      setAttempts((all) =>
        all.some((a) => a.id === attemptId)
          ? all
          : [
              ...all,
              {
                id: attemptId,
                origin,
                startedBy: null,
                trainState: "QUEUED",
                startTime: new Date().toISOString(),
                endTime: null,
                failureJunction: null,
                failureReason: null,
                steps: {},
                journal: null,
                graph: null,
              },
            ],
      );

      // Subscribe first, then read what was recorded before the subscription started.
      const stop = followRunProgress(attemptId, (event) => {
        if (at !== generation.current) return;
        if (event.retriedAs) return followNext.current(event.retriedAs);
        if (event.step) mergeSteps(attemptId, [event.step as unknown as Step]);
        if (event.state) updateState(attemptId, event.state, event.failure);
        if (event.problem) setProblem(event.problem.message);
      });
      subscriptions.current.set(attemptId, stop);
      void readRunProgress(attemptId)
        .then((read) => {
          if (at !== generation.current) return;
          mergeSteps(attemptId, read.steps as unknown as Step[]);
          updateState(attemptId, read.state, read.failure);
          if (read.retriedAs) followNext.current(read.retriedAs);
        })
        .catch(() => undefined);
    },
    [mergeSteps, updateState],
  );
  useEffect(() => {
    followNext.current = (attemptId) => follow(attemptId, "manifest");
  }, [follow]);

  // Once an attempt ends: read its decision journal one last time.
  useEffect(() => {
    for (const a of attempts) {
      if (!ENDED.has(a.trainState) || journaled.current.has(a.id)) continue;
      journaled.current.add(a.id);
      void readJournal(a.id).catch(() => undefined);
    }
  }, [attempts, readJournal]);

  const reset = useCallback(() => {
    generation.current++;
    subscriptions.current.forEach((stop) => stop());
    subscriptions.current.clear();
    journaled.current.clear();
    askedFor.current.clear();
    setAttempts([]);
    setRun(null);
    setProblem(null);
    setForkTaken("none");
  }, []);

  useEffect(() => {
    const live = subscriptions.current;
    return () => live.forEach((stop) => stop());
  }, []);

  const start = useCallback(
    async (scenario: Scenario, crashOnce: boolean, choice: Record<string, unknown>) => {
      reset();
      setStarting(true);
      try {
        const started = await startRun({ scenario, crashOnce, ...choice } as Parameters<typeof startRun>[0]);
        const output = started?.output;
        if (!output) throw new Error("The host answered no run.");
        setRun({ ...output, scenario } as RunInfo);
        follow(output.firstRunId, "manifest");
      } catch (error) {
        setProblem(`Could not start: ${(error as Error).message}`);
      } finally {
        setStarting(false);
      }
    },
    [follow, reset],
  );

  const phase: Phase = useMemo(() => {
    if (starting) return "starting";
    if (!run) return "idle";
    const last = attempts[attempts.length - 1];
    if (!last) return "starting";
    if (last.trainState === "COMPLETED") return "done";
    if (last.trainState === "FAILED") return attempts.length > (run.maxRetries ?? 2) ? "dead" : "backoff";
    return attempts.length > 1 ? "retrying" : "running";
  }, [attempts, run, starting]);

  const changeData = useCallback(async () => {
    if (!run) return;
    setForkTaken("changeData");
    try {
      await changeCaseData({ runId: run.runId });
    } catch (error) {
      setProblem((error as Error).message);
    }
  }, [run]);

  return { run, attempts, phase, forkTaken, problem, start, reset, changeData };
}
