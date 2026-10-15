// Plays back the PersistedOperations sample's recorded stories (src/data/persisted-operations-recordings.json,
// written by Trax.Samples' scripts/recordings/persisted-operations.mjs). Each story is the requests the sample's
// client sent its API, in order, with the status, the response and the time each took, and after an upload what
// the store held for greet_v1. Everything the page shows at a moment is worked out from the steps that had been
// sent or answered by then.

export type Actor = "app" | "stranger" | "operator";

export interface Step {
  actor: Actor;
  kind: "call" | "inline" | "upload";
  /** The document an upload sent. */
  document?: string;
  request: { headers: Record<string, string>; body: Record<string, unknown> };
  status: number;
  response: { data?: Record<string, unknown> | null; errors?: { message: string; extensions?: Record<string, unknown> }[] };
  ms: number;
  /** What the store held for greet_v1 after an upload. */
  storedAfter?: string | null;
}

export interface Story {
  key: string;
  title: string;
  storedBefore: string | null;
  steps: Step[];
}

export interface Recording {
  sources: { path: string; code: string }[];
  manifest: { id: string; document: string }[];
  stories: Story[];
}

/** When the first request goes out, the time between one answer and the next request, and the least a request shows as in flight. */
export const PACE = { first: 400, between: 1400, minInFlight: 450 };

export type Outcome = "waiting" | "answered" | "refused" | "accepted";

/** One request as the page shows it: who sent what, and what came back. */
export interface Card {
  key: string;
  actor: Actor;
  /** What was sent, in a line. */
  sent: string;
  outcome: Outcome;
  /** The answer in a line: the greeting, the refusal or "Accepted". */
  answer?: string;
  ms?: number;
}

export interface View {
  cards: Card[];
  /** The greeting the app shows, from its latest answered call, and whether a call is in flight. */
  phone: { greeting: string | null; calling: boolean };
  /** What the store holds for greet_v1 now, and the edit the operator has sent but not had answered, or last sent. */
  stored: string | null;
  /** `against` is what the store held when the edit was sent, so the change stays lit after it is taken. */
  proposed: { document: string; outcome: Outcome; against: string | null } | null;
  finished: boolean;
}

/** When each step is sent and answered, in milliseconds from the start of the story. */
export function scheduleOf(story: Story): { sendAt: number; answerAt: number }[] {
  const out: { sendAt: number; answerAt: number }[] = [];
  let at = PACE.first;
  for (const step of story.steps) {
    const answerAt = at + Math.max(step.ms, PACE.minInFlight);
    out.push({ sendAt: at, answerAt });
    at = answerAt + PACE.between;
  }
  return out;
}

export const durationOf = (story: Story | undefined) => (story ? (scheduleOf(story).at(-1)?.answerAt ?? 0) + 200 : 0);

export function viewAt(story: Story | undefined, now: number): View {
  const empty: View = { cards: [], phone: { greeting: null, calling: false }, stored: story?.storedBefore ?? null, proposed: null, finished: false };
  if (!story) return empty;
  const schedule = scheduleOf(story);
  const view = empty;
  story.steps.forEach((step, i) => {
    const { sendAt, answerAt } = schedule[i];
    if (now < sendAt) return;
    const answered = now >= answerAt;
    const result = answered ? outcomeOf(step) : null;
    view.cards.push({
      key: `${story.key}:${i}`,
      actor: step.actor,
      sent: sentText(step),
      outcome: result?.outcome ?? "waiting",
      answer: result?.text,
      ms: answered ? step.ms : undefined,
    });
    if (step.actor === "app") {
      view.phone.calling = !answered;
      if (result?.outcome === "answered") view.phone.greeting = result.text;
    }
    if (step.kind === "upload" && step.document) {
      view.proposed = { document: step.document, outcome: result?.outcome ?? "waiting", against: view.stored };
      if (answered && step.storedAfter !== undefined) view.stored = step.storedAfter;
    }
  });
  view.finished = now >= durationOf(story);
  return view;
}

/** What came back for a step, in a line. */
export function outcomeOf(step: Step): { outcome: Outcome; text: string } {
  const error = step.response.errors?.[0]?.message;
  if (step.kind === "upload") {
    const result = (step.response.data as UploadData | undefined)?.persistedOperations?.uploadPersistedOperation;
    if (result?.success) return { outcome: "accepted", text: "Accepted" };
    return { outcome: "refused", text: result?.errors?.[0]?.message ?? error ?? `HTTP ${step.status}` };
  }
  if (error) return { outcome: "refused", text: error };
  const greeting = (step.response.data as GreetData | undefined)?.discover?.greeting?.greet?.greeting;
  return greeting != null ? { outcome: "answered", text: greeting } : { outcome: "refused", text: `HTTP ${step.status}` };
}

function sentText(step: Step): string {
  const body = step.request.body as { id?: string; variables?: unknown; query?: string };
  if (step.kind === "call") return `${body.id} ${JSON.stringify(body.variables)}`;
  if (step.kind === "inline") return body.query ?? "";
  return `greet_v1 = ${step.document ?? ""}`;
}

type UploadData = { persistedOperations?: { uploadPersistedOperation?: { success: boolean; errors?: { message: string }[] } } };
type GreetData = { discover?: { greeting?: { greet?: { greeting?: string } } } };
