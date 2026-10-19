import { useState } from "react";
import { Link } from "react-router-dom";
import { useQuery } from "urql";
import { MACHINE_INSTANCE_TRANSITIONS } from "../graphql/queries";
import { enumLabel, formatMs, formatTime } from "../lib/format";
import { useNow } from "../lib/useNow";
import type {
  MachineInstanceTransition,
  MachineInstanceTransitions,
  SnapshotOwnerKind,
  SnapshotTransitionActor,
  SnapshotTransitionKind,
} from "../types";

const PAGE_SIZE = 25;

export const HISTORY_DOCS = "https://traxsharp.net/docs/statemachine";

/** The instance whose history the panel reads, named as `machineInstance` takes it. */
export interface TransitionHistoryKey {
  machine: string;
  ownerKind: SnapshotOwnerKind;
  id: string;
  /** Required for a user's draft: several users can hold a draft under one id. */
  rowId?: number | null;
}

interface TransitionsData {
  operations: { machineInstance: { rowId: number; transitions: MachineInstanceTransitions } | null };
}

const KIND_LABEL: Record<SnapshotTransitionKind, string> = {
  START: "Created",
  COMMAND: "Command",
  SAVE: "Saved",
  TIMER: "Timer",
  OUTCOME: "Run outcome",
};

const ACTOR_LABEL: Record<SnapshotTransitionActor, string> = {
  OWNER: "Owner",
  HOST: "Host",
  MACHINE: "Machine",
};

const KIND_TONE: Record<SnapshotTransitionKind, string> = {
  START: "bg-info-fg",
  COMMAND: "bg-accent-fg",
  SAVE: "bg-line-strong",
  TIMER: "bg-warn-fg",
  OUTCOME: "bg-ok-fg",
};

/**
 * The History panel of a state-machine instance (operations.machineInstance { transitions }): every
 * start, command, save, timer and run outcome Trax recorded, newest first, as a timeline. Each entry
 * names the command (or what wrote it when no command did), the states it moved between, who made it
 * (never which user), when, how long the instance had been in the state it left, and the run or child
 * instance it started. Older entries load on demand, a page at a time.
 *
 * Pass `instance` to read the history; pass `transitions` instead to show rows already read.
 */
export function TransitionHistory({
  instance,
  transitions,
}: {
  instance?: TransitionHistoryKey | null;
  transitions?: MachineInstanceTransition[];
}) {
  // Each "Load older" adds one page, read with the previous page's cursor.
  const [cursors, setCursors] = useState<(number | null)[]>([null]);
  const key = instance ? `${instance.machine}|${instance.ownerKind}|${instance.id}|${instance.rowId ?? ""}` : "";
  const [keyFor, setKeyFor] = useState(key);
  if (keyFor !== key) {
    setKeyFor(key);
    setCursors([null]);
  }

  return (
    <section aria-label="History" className="bg-surface rounded-lg border border-line p-5 mb-6">
      <h2 className="text-sm font-semibold text-fg mb-4">History</h2>
      {transitions ? (
        <Timeline rows={transitions} />
      ) : instance ? (
        <div>
          {cursors.map((after, i) => (
            <HistoryPage
              key={`${key}|${after ?? "first"}`}
              instance={instance}
              after={after}
              first={i === 0}
              last={i === cursors.length - 1}
              onOlder={(next) => setCursors((cur) => [...cur, next])}
            />
          ))}
        </div>
      ) : (
        <Empty />
      )}
    </section>
  );
}

function HistoryPage({
  instance,
  after,
  first,
  last,
  onOlder,
}: {
  instance: TransitionHistoryKey;
  after: number | null;
  first: boolean;
  last: boolean;
  onOlder: (cursor: number) => void;
}) {
  const [result] = useQuery<TransitionsData>({
    query: MACHINE_INSTANCE_TRANSITIONS,
    variables: {
      machine: instance.machine,
      ownerKind: instance.ownerKind,
      id: instance.id,
      rowId: instance.rowId ?? null,
      first: PAGE_SIZE,
      after,
    },
  });
  if (result.error) return <p className="text-sm text-danger-fg">{result.error.message}</p>;
  if (!result.data) return <p className="text-sm text-muted">Loading history…</p>;
  const page = result.data.operations.machineInstance?.transitions;
  const rows = page?.items ?? [];
  if (first && rows.length === 0) return <Empty />;
  return (
    <>
      <Timeline rows={rows} />
      {last && page?.nextCursor != null && (
        <button
          onClick={() => onOlder(page.nextCursor!)}
          className="mt-3 px-3 py-1 rounded-lg border border-line-strong text-sm"
        >
          Load older
        </button>
      )}
    </>
  );
}

function Empty() {
  return (
    <p className="text-sm text-muted">
      No history recorded for this instance. Every start, command, save, timer and run outcome is recorded from
      this version on.{" "}
      <a className="underline" href={HISTORY_DOCS} target="_blank" rel="noreferrer">
        How state machines move
      </a>
    </p>
  );
}

function Timeline({ rows }: { rows: MachineInstanceTransition[] }) {
  const now = useNow(30_000);
  if (rows.length === 0) return <Empty />;
  return (
    <ol className="relative border-l border-line ml-1.5">
      {rows.map((t) => (
        <Entry key={t.revision} t={t} now={now} />
      ))}
    </ol>
  );
}

function Entry({ t, now }: { t: MachineInstanceTransition; now: number }) {
  const atMs = new Date(t.at).getTime();
  const stayed = t.previousAt ? atMs - new Date(t.previousAt).getTime() : null;
  return (
    <li className="ml-4 pb-3 last:pb-0" data-testid={`transition-${t.revision}`}>
      <span
        aria-hidden
        className={`absolute -left-[5px] mt-1.5 h-2.5 w-2.5 rounded-full border-2 border-surface ${KIND_TONE[t.kind]}`}
      />
      <div className="flex flex-wrap items-baseline gap-x-2 text-sm">
        <span className="font-medium text-fg font-mono">{t.trigger ?? KIND_LABEL[t.kind]}</span>
        {t.trigger && <span className="text-xs text-muted">{KIND_LABEL[t.kind]}</span>}
        <span className="text-fg-2">
          {t.fromState == null ? (
            <>entered {t.toState}</>
          ) : t.fromState === t.toState ? (
            <>stayed in {t.toState}</>
          ) : (
            <>
              {t.fromState} → {t.toState}
            </>
          )}
        </span>
      </div>
      <div className="flex flex-wrap gap-x-3 text-xs text-muted mt-0.5">
        <span>by {ACTOR_LABEL[t.actor]}</span>
        <time dateTime={t.at} title={formatTime(t.at)}>
          {ago(now - atMs)}
        </time>
        {stayed != null && t.fromState != null && (
          <span title={`Time in ${t.fromState} since its previous write`}>
            {formatMs(stayed)} in {t.fromState}
          </span>
        )}
        <span>revision {t.revision}</span>
        <Started t={t} />
      </div>
    </li>
  );
}

function Started({ t }: { t: MachineInstanceTransition }) {
  const run = t.startedRun;
  const child = t.startedChild;
  if (child)
    return (
      <Link
        className="text-accent-fg hover:underline"
        to={`/machines/${encodeURIComponent(child.machine)}/${child.ownerKind === "USER" ? "user" : "system"}/${child.id}${
          child.ownerKind === "USER" ? `?row=${child.rowId}` : ""
        }`}
      >
        started {child.machine}
      </Link>
    );
  if (!run) return null;
  const state = run.trainState ? ` (${enumLabel(run.trainState).toLowerCase()})` : "";
  if (run.executionId != null)
    return (
      <Link className="text-accent-fg hover:underline" to={`/runs/${run.executionId}`}>
        started run {run.executionId}
        {state}
      </Link>
    );
  if (run.workQueueEntryId != null)
    return (
      <Link className="text-accent-fg hover:underline" to={`/runs/queued/${run.workQueueEntryId}`}>
        queued entry {run.workQueueEntryId}
        {state}
      </Link>
    );
  return <span>started a run no longer recorded</span>;
}

/** "just now", "5 min ago", "3 h ago", "2 d ago". */
function ago(ms: number): string {
  if (!Number.isFinite(ms) || ms < 60_000) return "just now";
  const min = Math.floor(ms / 60_000);
  if (min < 60) return `${min} min ago`;
  const h = Math.floor(min / 60);
  if (h < 48) return `${h} h ago`;
  return `${Math.floor(h / 24)} d ago`;
}
