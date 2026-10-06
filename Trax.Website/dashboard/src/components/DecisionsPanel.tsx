import { Fragment, useState } from "react";
import { useQuery } from "urql";
import { DECISIONS } from "../graphql/queries";
import { DetailPanel } from "./detail";
import { useKeyset } from "../lib/useKeyset";
import { formatTime, prettyJson, shortName } from "../lib/format";
import type { DecisionPage, DecisionRecord } from "../types";

const PAGE_SIZE = 25;

interface DecisionsData {
  operations: { decisions: DecisionPage };
}

/**
 * The decisions a run recorded (operations.decisions), in the order it made them: each question it
 * asked a decider, the answer it acted on or refused, and the tracks routing steps took on it. A
 * requeue that replays this run replays these answers.
 *
 * The API withholds what a [TraxSensitive] question gives away: an answer-withheld row keeps the
 * question but not the answer, refusal, shadows or routes, and every decision after a track taken on
 * such an answer keeps only when it was made. The panel says so rather than showing blanks.
 *
 * Pages by keyset: it asks for one row more than it shows, so Next is offered only when there is a
 * further page.
 */
export function DecisionsPanel({ metadataId }: { metadataId: number }) {
  const { afterId, isFirstPage, next, prev } = useKeyset();
  const [open, setOpen] = useState<number | null>(null);
  const [result] = useQuery<DecisionsData>({
    query: DECISIONS,
    variables: { metadataId, afterId, take: PAGE_SIZE + 1 },
  });
  const fetched = result.data?.operations?.decisions?.items ?? [];
  const rows = fetched.slice(0, PAGE_SIZE);
  const hasMore = fetched.length > PAGE_SIZE;
  const anyWithheld = rows.some((d) => d.answerWithheld || d.trackWithheld);

  return (
    <DetailPanel title="Decisions">
      {result.error && <p className="text-sm text-danger-fg mb-2">{result.error.message}</p>}
      {result.data && rows.length === 0 && isFirstPage && (
        <p className="text-sm text-muted">This run recorded no decisions.</p>
      )}
      {anyWithheld && (
        <p className="text-xs text-muted mb-3" data-testid="decisions-withheld-note">
          Answers to questions about a sensitive type are withheld, and so is everything about the decisions
          made after a track was taken on one, except when they were made.
        </p>
      )}
      {rows.length > 0 && (
        <table className="w-full text-sm" aria-label="Decisions">
          <thead className="text-left text-muted">
            <tr>
              <th className="py-1 font-medium">Question</th>
              <th className="py-1 font-medium">Kind</th>
              <th className="py-1 font-medium">Answer</th>
              <th className="py-1 font-medium">Decided by</th>
              <th className="py-1 font-medium">Decided</th>
              <th className="py-1" />
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {rows.map((d) => (
              <Fragment key={d.id}>
                <tr data-testid={`decision-${d.id}`}>
                  <td className="py-1 pr-3">
                    {d.trackWithheld ? (
                      <span className="text-muted italic">Withheld</span>
                    ) : (
                      <span className="font-mono text-xs text-fg">{d.questionKey}</span>
                    )}
                    {d.occurrence > 0 && (
                      <span className="ml-1 text-xs text-muted" title="Which asking of the question this was">
                        #{d.occurrence + 1}
                      </span>
                    )}
                    <Flags decision={d} />
                  </td>
                  <td className="py-1 pr-3 text-fg-2">{d.kind ? kindLabel(d.kind) : "—"}</td>
                  <td className="py-1 pr-3 max-w-xs">
                    <Answer decision={d} />
                  </td>
                  <td className="py-1 pr-3 text-fg-2 text-xs">
                    {d.replayed ? "Replayed" : d.decider ? shortName(d.decider) : "—"}
                    {d.model && <span className="block text-muted">{d.model}</span>}
                  </td>
                  <td className="py-1 pr-3 text-fg-2 text-xs whitespace-nowrap">
                    {formatTime(d.decidedAt)}
                  </td>
                  <td className="py-1 text-right">
                    {!d.trackWithheld && (
                      <button
                        onClick={() => setOpen((cur) => (cur === d.id ? null : d.id))}
                        aria-expanded={open === d.id}
                        className="text-xs text-accent-fg hover:underline"
                      >
                        {open === d.id ? "Hide" : "Details"}
                      </button>
                    )}
                  </td>
                </tr>
                {open === d.id && (
                  <tr>
                    <td colSpan={6} className="pb-3">
                      <DecisionDetails decision={d} />
                    </td>
                  </tr>
                )}
              </Fragment>
            ))}
          </tbody>
        </table>
      )}
      {(!isFirstPage || hasMore) && (
        <div className="flex justify-end gap-2 mt-3 text-sm">
          <button
            onClick={prev}
            disabled={isFirstPage}
            className="px-3 py-1 rounded-lg border border-line-strong disabled:opacity-40"
          >
            Previous
          </button>
          <button
            onClick={() => next(rows[rows.length - 1].id)}
            disabled={!hasMore}
            className="px-3 py-1 rounded-lg border border-line-strong disabled:opacity-40"
          >
            Next
          </button>
        </div>
      )}
    </DetailPanel>
  );
}

function Flags({ decision: d }: { decision: DecisionRecord }) {
  const flags: [string, string, string][] = [];
  if (d.replayed) flags.push(["Replayed", "The answer came from an earlier run, not a decider", "bg-info-soft text-info-fg"]);
  if (d.isRefused) flags.push(["Refused", "The run would not act on the answer and its step failed", "bg-danger-soft text-danger-fg"]);
  if (d.replayRefused) flags.push(["Replay refused", d.replayRefused, "bg-warn-soft text-warn-fg"]);
  if (d.trackWithheld) flags.push(["Track withheld", "Asked on a track taken on a withheld answer", "bg-raised text-fg-2"]);
  else if (d.answerWithheld) flags.push(["Answer withheld", "The question is about a sensitive type", "bg-raised text-fg-2"]);
  if (flags.length === 0) return null;
  return (
    <span className="ml-2 inline-flex flex-wrap gap-1 align-middle">
      {flags.map(([label, title, style]) => (
        <span key={label} title={title} className={`text-[10px] px-1.5 py-0.5 rounded-full ${style}`}>
          {label}
        </span>
      ))}
    </span>
  );
}

function Answer({ decision: d }: { decision: DecisionRecord }) {
  if (d.answerWithheld || d.trackWithheld)
    return <span className="text-muted italic">Withheld</span>;
  if (d.answer == null) return <span className="text-muted">None given</span>;
  const text = compactJson(d.answer);
  return (
    <span className="block truncate font-mono text-xs text-fg" title={text}>
      {text}
    </span>
  );
}

function DecisionDetails({ decision: d }: { decision: DecisionRecord }) {
  const blocks: [string, string | null][] = [
    ["Question", d.question],
    ["Answer", d.answerWithheld ? null : d.answer],
    ["Refused because", d.refused],
    ["Replay refused because", d.replayRefused],
    ["Shadow answers", d.shadows],
    ["Routes", d.routes],
  ];
  return (
    <div className="bg-inset rounded p-3 text-xs space-y-2">
      {d.answerWithheld && (
        <p className="text-muted">
          The answer, any refusal, shadow answers and routes are withheld: the question is about a sensitive type.
        </p>
      )}
      {blocks
        .filter(([, v]) => v != null && v !== "")
        .map(([label, v]) => (
          <div key={label}>
            <p className="text-muted">{label}</p>
            <pre className="whitespace-pre-wrap break-all font-mono text-fg">
              {prettyJson(v as string)}
            </pre>
          </div>
        ))}
      <dl className="grid grid-cols-2 gap-x-6 gap-y-1">
        <dt className="text-muted">Fingerprint</dt>
        <dd className="font-mono break-all">{d.fingerprint ?? "—"}</dd>
        <dt className="text-muted">State hash</dt>
        <dd className="font-mono break-all">{d.stateHash ?? "—"}</dd>
        <dt className="text-muted">Decider</dt>
        <dd className="break-all">{d.decider ?? "—"}</dd>
      </dl>
    </div>
  );
}

function kindLabel(kind: string): string {
  return { choice: "Choice", score: "Score", yes_no: "Yes / no" }[kind] ?? kind;
}

function compactJson(text: string): string {
  try {
    return JSON.stringify(JSON.parse(text));
  } catch {
    return text;
  }
}
