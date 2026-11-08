import Link from "next/link";
import { Arrow, Box, Figure } from "./Diagram";

// The support-ticket example from the Decisions pages in Trax.Docs (core/decisions.md, effect/decisions.md), drawn as
// the fork the workflow takes. The row below is the record of the same decision. The code is on the linked pages.
const tracks: { label: string; note: string; taken?: boolean; fallback?: boolean }[] = [
  { label: "Refund", note: "needs 0.7 sure", taken: true },
  { label: "Escalate", note: "open an incident" },
  { label: "Self-serve", note: "send a help article" },
  { label: "A person", note: "when the answer is under 0.5 sure", fallback: true },
];

const decisionRow: { name: string; value: string }[] = [
  { name: "question_key", value: "TicketTrack" },
  { name: "kind", value: "choice" },
  { name: "answer", value: "Refund, confidence 0.86" },
  { name: "model", value: "bespokelabs/Bespoke-Nimble-9B" },
  { name: "decider", value: "SystemOneDecider" },
  { name: "shadows", value: "ICandidateDecider: Refund, agreed" },
  { name: "routes", value: "Refund" },
  { name: "refused", value: "null" },
  { name: "replayed", value: "false" },
  { name: "state_hash", value: "k1:9c41e07b…" },
];

export default function Decisions() {
  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">A workflow can choose its path at run time</h2>
        <p className="mt-3 max-w-2xl text-text-secondary">
          A workflow lists every path it can take. At run time a decision model or a rule table answers a typed question
          about the work, and the workflow takes the matching path. An answer that is not sure enough takes the fallback
          instead.
        </p>

        <Figure caption="The model answered Refund, 0.86 sure, so the ticket took the refund path.">
          <div className="flex flex-col items-stretch gap-3 sm:flex-row sm:items-center">
            <Box label="A ticket" className="sm:w-36" />
            <Arrow />
            <Box label="Which team should handle this?" note="asked once, recorded" className="sm:w-60" />
            <Arrow />
            <div className="grid flex-1 grid-cols-2 gap-3">
              {tracks.map((t) => (
                <Box key={t.label} label={t.label} note={t.note} tone={t.taken ? "accent" : t.fallback ? "muted" : "plain"} />
              ))}
            </div>
          </div>
        </Figure>

        <div className="mt-16 grid gap-10 lg:grid-cols-[minmax(0,2fr)_minmax(0,3fr)] lg:gap-16">
          <div className="space-y-4 text-text-secondary">
            <h3 className="text-lg font-semibold text-text-primary">
              Every decision is recorded, and a requeue replays it
            </h3>
            <p>
              Each answer is saved before the workflow acts on it. Run it again
              and it takes the same paths from the saved answers, without
              asking the model. If the data changed since, the model is asked
              again.
            </p>
            <div className="flex flex-col gap-2 pt-2 text-sm font-medium">
              <Link
                href="/docs/core/decisions"
                className="text-accent hover:text-accent-hover"
              >
                How a workflow asks a question &rarr;
              </Link>
              <Link
                href="/docs/effect/decisions"
                className="text-accent hover:text-accent-hover"
              >
                How answers are recorded and replayed &rarr;
              </Link>
            </div>
          </div>

          <div className="min-w-0">
            <div className="rounded border border-border/50 bg-bg-secondary">
              <div className="border-b border-border/50 px-4 py-2">
                <span className="font-mono text-xs text-text-muted">
                  trax.decision, one row per question asked
                </span>
              </div>
              <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 p-4 font-mono text-[13px]">
                {decisionRow.map((f) => (
                  <div key={f.name} className="contents">
                    <dt className="text-text-muted">{f.name}</dt>
                    <dd className="min-w-0 text-text-primary [overflow-wrap:anywhere]">
                      {f.value}
                    </dd>
                  </div>
                ))}
              </dl>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
