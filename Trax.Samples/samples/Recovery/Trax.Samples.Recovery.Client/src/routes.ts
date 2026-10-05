import type { Attempt, Scenario, Step } from "./types";

/** One stop on a train's route: a junction, or a question whose answer picks one of its tracks. */
export type Stop =
  | { kind: "junction"; name: string }
  | { kind: "question"; key: string; label: string; tracks: { answer: string; junction: string }[] };

// The shape of each train's chain, as its Junctions() declares it. Kept beside the C# rather than read
// from a run, so the map can show the tracks a run did not take.
export const ROUTES: Record<Scenario, Stop[]> = {
  REFUND: [
    { kind: "junction", name: "LoadRefundCase" },
    {
      kind: "question",
      key: "ApproveRefund",
      label: "Pay without review?",
      tracks: [
        { answer: "Yes", junction: "IssuePayment" },
        { answer: "Unsure", junction: "QueueForReview" },
        { answer: "No", junction: "DeclineRefund" },
      ],
    },
    { kind: "junction", name: "NotifyCustomer" },
  ],
  RESEARCH: [
    { kind: "junction", name: "PlanResearch" },
    {
      kind: "question",
      key: "Source",
      label: "Where to look?",
      tracks: [
        { answer: "Web", junction: "SearchWeb" },
        { answer: "Papers", junction: "SearchPapers" },
        { answer: "Wiki", junction: "SearchWiki" },
      ],
    },
    {
      kind: "question",
      key: "Depth",
      label: "How deep?",
      tracks: [
        { answer: "Skim", junction: "SkimSources" },
        { answer: "CrossCheck", junction: "FetchFullTexts" },
      ],
    },
    { kind: "junction", name: "Summarize" },
  ],
};

export const questionsOf = (scenario: Scenario) =>
  ROUTES[scenario].filter((s): s is Extract<Stop, { kind: "question" }> => s.kind === "question");

export const junctionStep = (attempt: Attempt, name: string): Step | undefined =>
  Object.values(attempt.steps).find((s) => s.kind === "JUNCTION" && s.name === name);

export const questionStep = (attempt: Attempt, key: string): Step | undefined =>
  Object.values(attempt.steps).find((s) => s.kind !== "JUNCTION" && s.kind !== "ROUTE" && s.questionKey === key);

export const routeStep = (attempt: Attempt, key: string): Step | undefined =>
  Object.values(attempt.steps).find((s) => s.kind === "ROUTE" && s.questionKey === key);

const time = (iso: string | null | undefined) => (iso ? new Date(iso).getTime() : NaN);

/** How long the answer took to arrive: from the end of the step before the question to the answer. */
export function answerMs(attempt: Attempt, step: Step): number | null {
  const before = Object.values(attempt.steps)
    .filter((s) => s.position < step.position && s.endedAt)
    .sort((a, b) => b.position - a.position)[0];
  const from = time(before?.endedAt ?? attempt.startTime);
  const to = time(step.startedAt);
  return isNaN(from) || isNaN(to) ? null : Math.max(0, to - from);
}

export type Provenance = "model" | "replayed" | "afresh-on-purpose" | "afresh-state-changed" | "afresh";

/** Where an attempt's answer came from, from its junction event and the attempt's decision journal. */
export function provenanceOf(attempt: Attempt, index: number, step: Step): Provenance {
  if (step.replayed) return "replayed";
  const entry = attempt.journal?.decisions.find((d) => d.questionKey === step.questionKey);
  if (entry?.replayRefused) return "afresh-state-changed";
  if (index > 0 || attempt.origin === "requeue")
    return attempt.journal && attempt.journal.replayDecisionsOf == null ? "afresh-on-purpose" : "afresh";
  return "model";
}

export const PROVENANCE_TEXT: Record<Provenance, string> = {
  model: "asked the model",
  replayed: "replayed, model not called",
  "afresh-on-purpose": "asked again, on purpose",
  "afresh-state-changed": "asked again: the case changed",
  afresh: "asked again",
};
