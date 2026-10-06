import { useEffect, useRef, useState } from "react";
import { clearInspector, useInspector, type Channel, type InspectorEntry } from "../inspector";

type Filter = "all" | Channel;

const ARROWS = { out: "↑", in: "↓", info: "•" };

/**
 * "Under the hood": the GraphQL requests and WebSocket frames this tab sends and receives, as they
 * happen, each with what Trax did. Entries that belong to the same Trax run share its id, and hovering
 * one lights up the others, so a message can be followed from the mutation that ran its train to the
 * subscription event its lifecycle hook published.
 */
export function Inspector({ onClose }: { onClose(): void }) {
  const entries = useInspector();
  const [filter, setFilter] = useState<Filter>("all");
  const [showQuiet, setShowQuiet] = useState(false);
  const [open, setOpen] = useState<number | null>(null);
  const [hoverRun, setHoverRun] = useState<string | null>(null);
  const end = useRef<HTMLDivElement>(null);
  const pinned = useRef(true);

  const shown = entries.filter((e) => (filter === "all" || e.channel === filter) && (showQuiet || !e.quiet));

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
          {(["all", "http", "ws"] as Filter[]).map((f) => (
            <button key={f} className={filter === f ? "active" : ""} onClick={() => setFilter(f)}>
              {f === "all" ? "All" : f === "http" ? "HTTP" : "WebSocket"}
            </button>
          ))}
        </div>
        <label className="quiet-toggle" title="The room list's polling and socket keep-alives">
          <input type="checkbox" checked={showQuiet} onChange={(e) => setShowQuiet(e.target.checked)} />
          Polling
        </label>
        <button className="ghost" onClick={clearInspector}>
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
          <div className="inspector-empty">Send a message, open a room or switch user to see the traffic.</div>
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
