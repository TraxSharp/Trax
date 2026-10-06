import { useEffect, useRef, useState } from "react";
import { clearInspector, useInspector, type InspectorEntry } from "./inspectorLog";

type Filter = "all" | "refused";

const ARROWS = { out: "↑", in: "↓", info: "•" };

/**
 * "Under the hood": every stateMachine mutation this tab sends and what the server answered, as they
 * happen, each with what Trax did. A request and its response share the Trax run's external id, and
 * hovering one lights up the other.
 */
export function Inspector({ onClose }: { onClose(): void }) {
  const entries = useInspector();
  const [filter, setFilter] = useState<Filter>("all");
  const [open, setOpen] = useState<number | null>(null);
  const [hoverRun, setHoverRun] = useState<string | null>(null);
  const end = useRef<HTMLDivElement>(null);
  const pinned = useRef(true);

  const shown = entries.filter((e) => filter === "all" || (e.failed && e.direction === "in"));

  // Follow new entries while the list is scrolled to the bottom; leave it alone once the reader scrolls up.
  useEffect(() => {
    if (pinned.current) end.current?.scrollIntoView({ block: "nearest" });
  }, [shown.length]);

  return (
    <aside className="inspector">
      <header className="inspector-header">
        <div>
          <h2>Under the hood</h2>
          <p>What this tab sends to Trax and gets back, live.</p>
        </div>
        <button className="icon-button" onClick={onClose} aria-label="Close the inspector">
          ✕
        </button>
      </header>

      <div className="inspector-toolbar">
        <div className="segmented">
          {(["all", "refused"] as Filter[]).map((f) => (
            <button key={f} className={filter === f ? "active" : ""} onClick={() => setFilter(f)}>
              {f === "all" ? "All" : "Refused"}
            </button>
          ))}
        </div>
        <button className="ghost push-right" onClick={clearInspector}>
          Clear
        </button>
      </div>

      <div
        className="inspector-list"
        onScroll={(e) => {
          const el = e.currentTarget;
          pinned.current = el.scrollHeight - el.scrollTop - el.clientHeight < 40;
        }}
      >
        {shown.length === 0 && (
          <div className="inspector-empty">Press a button on either machine to see the request and what the server did.</div>
        )}
        {shown.map((e) => (
          <Row
            key={e.id}
            entry={e}
            expanded={open === e.id}
            onToggle={() => setOpen(open === e.id ? null : e.id)}
            lit={!!e.runId && e.runId === hoverRun}
            onHoverRun={setHoverRun}
          />
        ))}
        <div ref={end} />
      </div>
    </aside>
  );
}

function Row({
  entry,
  expanded,
  onToggle,
  lit,
  onHoverRun,
}: {
  entry: InspectorEntry;
  expanded: boolean;
  onToggle(): void;
  lit: boolean;
  onHoverRun(run: string | null): void;
}) {
  const time = new Date(entry.at).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", second: "2-digit" });
  return (
    <div
      className={`entry entry-${entry.direction} ${entry.failed ? "entry-failed" : ""} ${lit ? "entry-lit" : ""}`}
      onMouseEnter={() => entry.runId && onHoverRun(entry.runId)}
      onMouseLeave={() => entry.runId && onHoverRun(null)}
    >
      <button className="entry-summary" onClick={onToggle} disabled={entry.detail === undefined}>
        <span className={`entry-arrow arrow-${entry.direction}`}>{ARROWS[entry.direction]}</span>
        <span className={`chip chip-${entry.channel}`}>{entry.channel === "http" ? "HTTP" : "WS"}</span>
        <span className="entry-title">{entry.title}</span>
        {entry.durationMs !== undefined && <span className="entry-ms">{entry.durationMs} ms</span>}
        <span className="entry-time">{time}</span>
      </button>
      {(entry.note || entry.runId) && (
        <div className="entry-note">
          {entry.note}
          {entry.runId && <span className="run-chip">run {entry.runId.slice(0, 8)}</span>}
        </div>
      )}
      {expanded && entry.detail !== undefined && (
        <pre className="entry-detail">
          {formatDetail(entry.detail)}
        </pre>
      )}
    </div>
  );
}

/** A GraphQL document reads best as itself, with its variables after it; everything else as JSON. */
function formatDetail(detail: unknown): string {
  if (typeof detail === "string") return detail;
  const d = detail as { query?: unknown; variables?: unknown; type?: string; id?: string; payload?: { query?: unknown; variables?: unknown } };
  const doc = typeof d?.query === "string" ? d : typeof d?.payload?.query === "string" ? d.payload : null;
  if (doc) {
    const head = d.type ? `${d.type}${d.id ? ` #${d.id}` : ""}\n\n` : "";
    return `${head}${String(doc.query).trim()}\n\nvariables ${JSON.stringify(doc.variables ?? {}, null, 2)}`;
  }
  return JSON.stringify(unpack(detail), null, 2);
}

/** Event payloads arrive as JSON inside a string; show them as the structure they are. */
function unpack(value: unknown): unknown {
  if (typeof value === "string" && /^\s*[[{]/.test(value)) {
    try {
      return unpack(JSON.parse(value));
    } catch {
      return value;
    }
  }
  if (Array.isArray(value)) return value.map(unpack);
  if (value && typeof value === "object")
    return Object.fromEntries(Object.entries(value).map(([k, v]) => [k, unpack(v)]));
  return value;
}
