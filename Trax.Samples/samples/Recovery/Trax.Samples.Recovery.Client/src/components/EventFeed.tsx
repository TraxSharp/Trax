import { useEffect, useMemo, useRef } from "react";
import type { Attempt, ConsoleLine } from "../types";

interface Props {
  attempts: Attempt[];
  lines: ConsoleLine[];
  labelOf(attempt: Attempt, index: number): string;
}

interface Row {
  key: string;
  at: string;
  who: string;
  what: string;
  detail: string;
  tone: string;
}

/** Every junction event the page received, in arrival order, beside what the page itself did. */
export function EventFeed({ attempts, lines, labelOf }: Props) {
  const rows = useMemo(() => {
    const all: Row[] = lines.map((l) => ({ key: l.key, at: l.at, who: "page", what: "", detail: l.text, tone: l.tone }));
    attempts.forEach((a, index) => {
      for (const s of Object.values(a.steps)) {
        const at = s.endedAt ?? s.startedAt;
        const name = s.nameWithheld ? "(withheld)" : s.kind === "JUNCTION" ? s.name : s.questionKey ?? s.name;
        const detail =
          s.kind === "JUNCTION"
            ? s.state === "FAILED"
              ? (s.failureException ?? "failed")
              : s.state === "COMPLETED"
                ? `${Math.round(s.durationMs ?? 0)} ms`
                : "running"
            : s.kind === "ROUTE"
              ? `track ${s.answer}`
              : `answer ${s.answer}${s.confidence != null ? `, confidence ${s.confidence.toFixed(2)}` : ""}${s.replayed ? ", replayed" : ""}`;
        all.push({
          key: `${a.id}:${s.position}:${s.state}`,
          at,
          who: labelOf(a, index),
          what: `#${s.position} ${s.kind} ${name}`,
          detail,
          tone: s.state === "FAILED" ? "error" : s.replayed ? "replay" : s.kind === "JUNCTION" ? "info" : "model",
        });
      }
    });
    return all.sort((x, y) => x.at.localeCompare(y.at));
  }, [attempts, lines, labelOf]);

  const end = useRef<HTMLDivElement>(null);
  useEffect(() => {
    end.current?.scrollIntoView({ block: "nearest" });
  }, [rows.length]);

  return (
    <div className="feed">
      {rows.length === 0 && <p className="hint">The onJunctionEvent subscription's events appear here as they arrive.</p>}
      {rows.map((r) => (
        <div key={r.key} className={`feed-row ${r.tone}`}>
          <span className="feed-time">{new Date(r.at).toLocaleTimeString([], { hour12: false })}</span>
          <span className="feed-who">{r.who}</span>
          <span className="feed-what">{r.what}</span>
          <span className="feed-detail">{r.detail}</span>
        </div>
      ))}
      <div ref={end} />
    </div>
  );
}
