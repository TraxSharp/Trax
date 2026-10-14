import { useCallback, useEffect, useRef, useState } from "react";
import { useClient } from "urql";
import { JUNCTION_RUNS } from "../graphql/queries";
import { ON_JUNCTION_EVENT } from "../graphql/subscriptions";
import {
  MAX_JUNCTION_STEPS,
  attemptOf,
  formatConfidence,
  incrementalCursor,
  kindLabel,
  layoutTimeline,
  mergeSteps,
  stepFailure,
} from "../lib/junctionSteps";
import { useRefetchOnReconnect } from "../lib/useRefetchOnReconnect";
import type { JunctionEvent, JunctionStep, TrainState } from "../types";

const TERMINAL: TrainState[] = ["COMPLETED", "FAILED", "CANCELLED"];

const BAR_STYLE: Record<JunctionStep["state"], string> = {
  IN_PROGRESS: "bg-info animate-pulse",
  COMPLETED: "bg-ok",
  FAILED: "bg-danger",
  CANCELLED: "bg-warn",
};

interface JunctionRunsData {
  operations: { junctionRuns: JunctionStep[] };
}

/**
 * Per-step timeline of one run, read from operations.junctionRuns (written when the host enables
 * AddJunctionEvents) and kept current by onJunctionEvent. The first read loads up to 500 steps;
 * later reads are incremental (afterPosition from the first step still in progress), and a gap in
 * the event sequence or a socket reconnect triggers one. Mirrors the Blazor JunctionTimeline.
 */
export function JunctionTimeline({
  metadataId,
  startTime,
  endTime,
  trainState,
}: {
  metadataId: number;
  startTime: string;
  endTime: string | null;
  trainState: TrainState;
}) {
  const client = useClient();
  const [steps, setSteps] = useState<JunctionStep[]>([]);
  const [hasMore, setHasMore] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const stepsRef = useRef(steps);
  useEffect(() => {
    stepsRef.current = steps;
  });

  // full: read from the beginning (first load, and once the run has finished); otherwise read only
  // what can have changed.
  const read = useCallback(
    async (full: boolean) => {
      const held = full ? [] : stepsRef.current;
      const after = full ? null : incrementalCursor(held);
      const kept = after == null ? 0 : held.filter((s) => s.position <= after).length;
      const take = MAX_JUNCTION_STEPS - kept;
      if (take <= 0) return;
      const r = await client
        .query<JunctionRunsData>(
          JUNCTION_RUNS,
          { metadataId, afterPosition: after, take },
          { requestPolicy: "network-only" },
        )
        .toPromise();
      if (r.error) {
        setError(r.error.message);
        return;
      }
      setError(null);
      const rows = r.data?.operations?.junctionRuns ?? [];
      const next = mergeSteps(full ? [] : stepsRef.current, rows);
      stepsRef.current = next;
      setSteps(next);
      setLoaded(true);
      // A full page: ask for one more past it, so a run with more steps says so.
      if (next.length >= MAX_JUNCTION_STEPS) {
        const probe = await client
          .query<JunctionRunsData>(
            JUNCTION_RUNS,
            { metadataId, afterPosition: next[MAX_JUNCTION_STEPS - 1].position, take: 1 },
            { requestPolicy: "network-only" },
          )
          .toPromise();
        setHasMore((probe.data?.operations?.junctionRuns ?? []).length > 0);
      } else if (full) setHasMore(false);
    },
    [client, metadataId],
  );

  // First load, and a full read once the run is seen finished (a step can be written a moment
  // after the run ends).
  const finished = TERMINAL.includes(trainState);
  useEffect(() => {
    void read(true);
  }, [read, finished]);

  useRefetchOnReconnect(() => void read(false));

  // Live steps. Each event carries the whole step, so it is upserted by position; a sequence that
  // skips means events were lost, so read again.
  useEffect(() => {
    let lastSequence: number | null = null;
    const sub = client
      .subscription<{ onJunctionEvent: JunctionEvent }>(ON_JUNCTION_EVENT, { metadataId })
      .subscribe((result) => {
        const event = result.data?.onJunctionEvent;
        if (!event || event.metadataId !== metadataId) return;
        const gap = lastSequence != null && event.sequence > lastSequence + 1;
        lastSequence = event.sequence;
        const held = stepsRef.current;
        const fits =
          held.length < MAX_JUNCTION_STEPS ||
          held.some((s) => s.position === event.junction.position);
        if (fits) {
          const next = mergeSteps(held, [event.junction]);
          stepsRef.current = next;
          setSteps(next);
          setLoaded(true);
        } else setHasMore(true);
        if (gap) void read(false);
      });
    return () => sub.unsubscribe();
  }, [client, metadataId, read]);

  const now = useTicker(!endTime);
  const rows = layoutTimeline(steps, startTime, endTime, now);
  const attempt = attemptOf(steps);

  return (
    <section
      aria-label="Junction timeline"
      className="bg-surface rounded-lg border border-line p-5 mb-6"
    >
      <div className="flex items-center gap-2 mb-3">
        <h2 className="text-sm font-semibold text-fg">
          Junction timeline
        </h2>
        {attempt != null && (
          <span className="text-xs px-2 py-0.5 rounded-full border border-info-line text-info-fg">
            attempt {attempt}
          </span>
        )}
      </div>

      {error && <p className="text-sm text-danger-fg mb-2">{error}</p>}

      {rows.length === 0 ? (
        <p className="text-sm text-muted">
          {loaded || error
            ? "No steps recorded for this run. Steps are recorded only when the host enables junction events with AddJunctionEvents(), and a run that has not started has none yet."
            : "Loading steps…"}
        </p>
      ) : (
        <>
          {hasMore && (
            <p className="text-sm text-warn-fg mb-2">
              Showing the first {steps.length} steps; this run recorded more.
            </p>
          )}
          <ol className="space-y-1.5">
            {rows.map(({ step, left, width, isPoint, duration }) => {
              const withheldName = step.nameWithheld;
              const failure = stepFailure(step);
              return (
                <li
                  key={step.position}
                  data-position={step.position}
                  data-state={step.state}
                  className="grid grid-cols-[minmax(0,2fr)_minmax(0,3fr)] gap-3 items-center text-sm"
                >
                  <div className="min-w-0">
                    <div
                      className="flex items-center gap-1.5 truncate"
                      title={withheldName ? "withheld" : step.name}
                    >
                      <span aria-hidden className="text-muted">
                        {step.kind === "JUNCTION" ? "▬" : step.kind === "ROUTE" ? "⤳" : "?"}
                      </span>
                      <span className="text-xs text-muted">
                        #{step.position}
                      </span>
                      <span
                        className={`font-medium truncate ${
                          withheldName && step.kind === "JUNCTION"
                            ? "italic text-muted"
                            : "text-fg"
                        }`}
                      >
                        {step.kind !== "JUNCTION"
                          ? kindLabel(step.kind)
                          : withheldName
                            ? "withheld"
                            : step.name}
                      </span>
                    </div>
                    <div className="flex flex-wrap items-center gap-x-2 text-xs text-muted">
                      {step.trackPosition != null && (
                        <span>on track of step #{step.trackPosition}</span>
                      )}
                      {step.kind === "JUNCTION" ? (
                        <span>{duration}</span>
                      ) : (
                        <Decision step={step} />
                      )}
                      {failure && (
                        <span className="text-danger-fg">{failure}</span>
                      )}
                    </div>
                  </div>
                  <div className="relative h-3 rounded bg-raised">
                    {isPoint ? (
                      <div
                        className={`absolute top-0 h-3 w-3 -ml-1.5 rounded-full ${BAR_STYLE[step.state]}`}
                        style={{ left: pct(left) }}
                        title={step.state}
                      />
                    ) : (
                      <div
                        className={`absolute top-0 h-3 rounded ${BAR_STYLE[step.state]}`}
                        style={{ left: pct(left), width: pct(width) }}
                        title={`${step.state} · ${duration}`}
                      />
                    )}
                  </div>
                </li>
              );
            })}
          </ol>
        </>
      )}
    </section>
  );
}

// A question or route: its key, its answer (or "withheld"), the confidence, and a replayed badge.
// A step on a withheld track shows neither its key nor its answer.
function Decision({ step }: { step: JunctionStep }) {
  const question = step.questionKey ?? step.name;
  const answerHidden = step.answerWithheld || step.nameWithheld;
  return (
    <>
      <span className={step.nameWithheld ? "italic" : ""} title={step.nameWithheld ? undefined : question}>
        {step.nameWithheld ? "withheld" : question}
      </span>
      <span aria-hidden>→</span>
      {answerHidden ? (
        <span className="italic">withheld</span>
      ) : (
        <>
          <span className="font-medium text-fg">
            {step.answer ?? "no answer"}
          </span>
          {step.confidence != null && <span>{formatConfidence(step.confidence)} confidence</span>}
        </>
      )}
      {step.replayed && (
        <span className="px-1.5 rounded-full border border-line-strong">
          replayed
        </span>
      )}
    </>
  );
}

function pct(fraction: number): string {
  return `${(fraction * 100).toFixed(3)}%`;
}

// The current time, moving once a second only while the run is still going, so a running step's
// bar grows and a finished timeline does not re-render.
function useTicker(active: boolean): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!active) return;
    const id = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, [active]);
  return now;
}
