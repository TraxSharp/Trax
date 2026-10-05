"use client";

import { useLayoutEffect, useMemo, useRef, useState } from "react";
import { useFitCode } from "./useFitCode";
import data from "@/data/persisted-operations-recordings.json";
import {
  ACTIONS,
  act,
  highlightFor,
  offered,
  outcomeOf,
  rangeOf,
  start,
  type Caller,
  type Entry,
  type Exchange,
  type Recording,
  type Session,
  type StoreState,
} from "@/lib/persisted-operations-replay";

// Recorded from the PersistedOperations sample's API by Trax.Samples' scripts/recordings/persisted-operations.mjs,
// which writes this file with its --copy-to option. Re-record when the sample's host, trains or manifest change.
const recording = data as unknown as Recording;

const GROUPS: { as: Caller; label: string; note: string }[] = [
  { as: "client", label: "As a client", note: "No key" },
  { as: "operator", label: "As the operator", note: "X-Api-Key: operator key" },
  { as: "anonymous", label: "Managing without a key", note: "No key" },
];

const STATE_LABEL: Record<StoreState, string> = {
  empty: "Nothing uploaded",
  uploaded: "Manifest uploaded",
  hotfixed: "greet_v1 hot-fixed",
};

const labelOf = (key: string) => ACTIONS.find((a) => a.key === key)?.label ?? key;

export default function PersistedOperationsReplay() {
  const [session, setSession] = useState<Session>(() => start(recording));
  const latest = session.log[session.log.length - 1] ?? null;

  return (
    <div className="grid gap-4 lg:grid-cols-[250px_minmax(0,1fr)_minmax(0,1fr)]">
      {/* Actions */}
      <div className="flex flex-col gap-5 rounded-lg border border-border bg-bg-secondary p-4 text-sm">
        <div className="flex flex-col gap-1.5">
          <span className="text-[11px] font-semibold uppercase tracking-wider text-text-muted">Store</span>
          <span
            className={`self-start rounded-full border px-2.5 py-0.5 text-xs ${
              session.state === "empty"
                ? "border-border text-text-muted"
                : session.state === "hotfixed"
                  ? "border-signal/50 text-signal"
                  : "border-accent/50 text-accent-bright"
            }`}
          >
            {STATE_LABEL[session.state]}
          </span>
        </div>
        {GROUPS.map((group) => (
          <div key={group.as} className="flex flex-col gap-2">
            <span className="text-[11px] font-semibold uppercase tracking-wider text-text-muted">
              {group.label} <span className="font-normal normal-case tracking-normal">({group.note})</span>
            </span>
            {ACTIONS.filter((a) => a.as === group.as).map((a) => (
              <button
                key={a.key}
                disabled={!offered(recording, session, a.key)}
                onClick={() => setSession((s) => act(recording, s, a.key))}
                title={offered(recording, session, a.key) ? undefined : "Upload the manifest first"}
                className="rounded-md border border-border bg-bg-tertiary px-3 py-1.5 text-left text-[13px] text-text-primary transition-colors hover:border-accent/60 disabled:cursor-default disabled:opacity-35 disabled:hover:border-border"
              >
                {a.label}
              </button>
            ))}
          </div>
        ))}
        <button
          onClick={() => setSession(start(recording))}
          disabled={session.log.length === 0}
          className="rounded-md border border-border px-3 py-1.5 text-left text-[13px] text-text-secondary transition-colors hover:border-accent/60 disabled:cursor-default disabled:opacity-35"
        >
          Reset to an empty store
        </button>
      </div>

      <TrafficPanel log={session.log} />

      <StorePanel session={session} />

      <div className="min-w-0 lg:col-span-3">
        <CodePanel entry={latest} />
      </div>

      <p className="text-xs leading-relaxed text-text-muted lg:col-span-3">
        Recorded from the PersistedOperations sample&apos;s API: every request above is the one the page sends, and every
        response, refusal and stored document is the one the real server gave back.
      </p>
    </div>
  );
}

function PanelTitle({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex items-center gap-3 border-b border-border px-4 py-2 text-[11px] font-semibold uppercase tracking-wider text-text-muted">
      {children}
    </div>
  );
}

function TrafficPanel({ log }: { log: Entry[] }) {
  const box = useRef<HTMLDivElement>(null);
  // Scroll the panel itself, never the page.
  useLayoutEffect(() => {
    if (box.current) box.current.scrollTop = box.current.scrollHeight;
  }, [log.length]);

  return (
    <div className="flex min-w-0 flex-col overflow-hidden rounded-lg border border-border bg-bg-secondary">
      <PanelTitle>HTTP</PanelTitle>
      <div ref={box} className="h-[520px] overflow-y-auto px-4 py-3 lg:h-[640px]">
        {log.length === 0 && (
          <p className="text-sm text-text-muted">
            Pick an action. The store starts empty, so a call by id fails until the manifest is uploaded.
          </p>
        )}
        {log.map((entry, i) => (
          <div key={i} className={`border-border ${i > 0 ? "mt-4 border-t pt-4" : ""}`}>
            <p className="mb-2 text-xs font-medium text-text-primary">{labelOf(entry.key)}</p>
            {entry.exchanges.map((exchange, j) => (
              <ExchangeView key={j} exchange={exchange} latest={i === log.length - 1} />
            ))}
          </div>
        ))}
      </div>
    </div>
  );
}

function ExchangeView({ exchange, latest }: { exchange: Exchange; latest: boolean }) {
  const { request } = exchange;
  const outcome = outcomeOf(exchange);
  const { query, ...rest } = request.body as { query?: string } & Record<string, unknown>;
  return (
    <div className={`mb-3 font-mono text-[11.5px] leading-relaxed ${latest ? "" : "opacity-60"}`}>
      <div className="text-text-secondary">
        <span className="text-info">{request.method}</span> {request.path}
      </div>
      {Object.entries(request.headers).map(([name, value]) => (
        <div key={name} className="text-text-muted">
          {name}: <span className={name === "X-Api-Key" ? "text-signal" : ""}>{value}</span>
        </div>
      ))}
      <pre className="mt-1 whitespace-pre-wrap rounded border border-border bg-bg-primary p-2 text-text-secondary [overflow-wrap:anywhere]">
        {query != null ? `${query.trim()}\n\n` : ""}
        {JSON.stringify(rest, null, 2)}
      </pre>
      <div className="mt-2 flex flex-wrap items-center gap-2">
        <span
          className={`rounded-full border px-2 py-0.5 font-sans text-[11px] ${
            outcome.ok ? "border-accent/40 text-accent-bright" : "border-derail/40 text-derail"
          }`}
        >
          {exchange.status} {outcome.ok ? "ok" : (outcome.code ?? "refused")}
        </span>
        <span className="font-sans text-[11px] text-text-muted">{exchange.durationMs} ms</span>
      </div>
      <pre
        className={`mt-1 whitespace-pre-wrap rounded border p-2 [overflow-wrap:anywhere] ${
          outcome.ok ? "border-border bg-bg-primary text-text-primary" : "border-derail/30 bg-derail-muted/40 text-derail"
        }`}
      >
        {JSON.stringify(exchange.response, null, 2)}
      </pre>
    </div>
  );
}

function StorePanel({ session }: { session: Session }) {
  const ids = recording.manifest.map((m) => m.id);
  return (
    <div className="min-w-0 self-start overflow-hidden rounded-lg border border-border bg-bg-secondary">
      <PanelTitle>What the store holds</PanelTitle>
      <div className="space-y-3 px-4 py-3 text-xs">
        {ids.map((id) => {
          const stored = session.store[id];
          const manifest = recording.manifest.find((m) => m.id === id)?.document;
          const fixed = stored != null && stored.document !== manifest;
          return (
            <div key={id}>
              <div className="flex flex-wrap items-center gap-2">
                <span className="font-mono text-text-primary">{id}</span>
                {stored == null ? (
                  <span className="text-text-muted">not stored</span>
                ) : (
                  <>
                    {fixed && (
                      <span className="rounded-full border border-signal/40 px-2 py-px text-[11px] text-signal">hot-fixed</span>
                    )}
                    <span className="font-mono text-[11px] text-text-muted" title={stored.shapeFingerprint}>
                      shape {stored.shapeFingerprint.slice(0, 8)}
                    </span>
                  </>
                )}
              </div>
              {stored != null && (
                <pre className="mt-1 whitespace-pre-wrap rounded border border-border bg-bg-primary p-2 font-mono text-[11px] leading-relaxed text-text-secondary [overflow-wrap:anywhere]">
                  {stored.document}
                </pre>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}

// The words that switch persisted operations on and gate them, picked out in the code.
const TOKEN =
  /(\.(?:UsePersistedOperations|RequirePersisted|AllowOperationsMatching|GateOperations|UseDatabase|SingleNode)\b)|(\[Trax[A-Za-z]*)|("[^"]*")|(\/\/.*$)|(\b(?:public|class|protected|override|if)\b)/g;

function highlight(line: string) {
  const parts: React.ReactNode[] = [];
  let last = 0;
  for (const m of line.matchAll(TOKEN)) {
    if (m.index > last) parts.push(line.slice(last, m.index));
    const tone = m[1]
      ? "text-signal"
      : m[2]
        ? "text-info"
        : m[3]
          ? "text-accent-bright"
          : m[4]
            ? "text-text-muted/70 italic"
            : "text-text-muted";
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

function CodePanel({ entry }: { entry: Entry | null }) {
  const { file, needle } = entry ? highlightFor(entry) : { file: recording.sources[0].path, needle: "" };
  const source = recording.sources.find((s) => s.path === file) ?? recording.sources[0];
  const lines = useMemo(() => source.code.split("\n"), [source.code]);
  const [first, end] = useMemo(() => rangeOf(source.path, source.code), [source]);
  const indent = useMemo(
    () => Math.min(...lines.slice(first, end).filter((l) => l.trim()).map((l) => l.search(/\S/))),
    [lines, first, end],
  );
  const at = needle ? lines.findIndex((l) => l.includes(needle)) : -1;
  const ok = entry ? entry.exchanges.every((e) => outcomeOf(e).ok) : true;
  const tone = ok ? "border-accent-bright bg-accent/15" : "border-derail bg-derail/15";

  // Bring the highlighted line into view by scrolling the panel, never the page.
  const pane = useRef<HTMLPreElement>(null);
  useFitCode(pane, source.path, 11.5);
  const marked = useRef<HTMLDivElement>(null);
  useLayoutEffect(() => {
    const p = pane.current;
    const m = marked.current;
    if (!p) return;
    p.scrollTop = m ? Math.max(0, m.offsetTop - p.clientHeight / 2 + m.clientHeight / 2) : 0;
  }, [file, at, entry]);

  return (
    <div className="flex min-w-0 flex-col overflow-hidden rounded-lg border border-border bg-bg-secondary">
      <PanelTitle>
        Code <span className="font-mono normal-case tracking-normal text-text-secondary">{source.path.split("/").pop()}</span>
      </PanelTitle>
      <pre ref={pane} className="relative max-h-[360px] overflow-auto py-3 font-mono text-[11.5px] leading-[1.7] text-text-secondary">
        {lines.slice(first, end).map((line, offset) => {
          const i = first + offset;
          return (
            <div
              key={i}
              ref={i === at ? marked : undefined}
              className={`flex w-max min-w-full border-l-2 pr-4 transition-colors duration-200 ${i === at ? tone : "border-transparent"}`}
            >
              <span className="mr-4 w-8 shrink-0 select-none text-right text-text-muted/60">{i + 1}</span>
              <span className="whitespace-pre">
                {highlight(line.slice(indent))}
              </span>
            </div>
          );
        })}
      </pre>
    </div>
  );
}
