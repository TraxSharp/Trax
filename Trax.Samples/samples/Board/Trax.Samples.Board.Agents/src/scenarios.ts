// The scripted runs: four tickets, each worked through the lanes by the same five clients. The agents are scripted
// (what each sends is written here), but everything they send goes to the real host through the generated client,
// and every answer, refusal and check result is the host's.

import { fileTicket, configureTrax } from "./trax/index.g";
import { GRAPHQL_URL, Log, SOCKET_URL, Worker, type WorkerSpec } from "./agent";

// The demo keys the host registers in Development (Trax.Samples.Board/Auth/BoardKeys.cs).
export const WORKERS: WorkerSpec[] = [
  { name: "triage-bot", id: "triage-bot", role: "triage", kind: "agent", lane: "triage", key: "board-triage-bot-key-do-not-use-in-production" },
  { name: "repro-bot", id: "repro-bot", role: "reproducer", kind: "agent", lane: "reproduce", key: "board-repro-bot-key-do-not-use-in-production" },
  { name: "fix-bot", id: "fix-bot", role: "fixer", kind: "agent", lane: "fix", key: "board-fix-bot-key-do-not-use-in-production" },
  { name: "qa-bot", id: "qa-bot", role: "qa", kind: "agent", lane: "qa", key: "board-qa-bot-key-do-not-use-in-production" },
  { name: "Sam", id: "sam", role: "reviewer", kind: "person", lane: "review", key: "board-sam-key-do-not-use-in-production" },
];

export interface Crew {
  triage: Worker;
  repro: Worker;
  fix: Worker;
  qa: Worker;
  sam: Worker;
}

export interface Scenario {
  key: string;
  title: string;
  caption: string;
  report: string;
  ticket: string;
  run: (crew: Crew, ticket: string) => Promise<void>;
}

export interface RecordedScenario {
  key: string;
  title: string;
  caption: string;
  tickets: { id: string; title: string; state: string }[];
  events: Log["events"];
}

async function triage(crew: Crew, t: string): Promise<void> {
  await crew.triage.claim(t, "claim: triage.");
  await crew.triage.send(t, "Triage", { area: "shipping" }, "classified: shipping, threshold boundary. route: reproduce.");
}

async function review(crew: Crew, t: string): Promise<void> {
  await crew.sam.claim(t, "Claiming the review.");
  await crew.sam.send(t, "Approve", undefined, "Approved.");
}

export const SCENARIOS: Scenario[] = [
  {
    key: "shipping-boundary",
    title: "Orders of exactly $50 still pay shipping",
    caption:
      "Every lane is worked by its own client. fix-bot tries to skip ahead and to approve its own fix, and the server refuses both.",
    report: "boundary",
    ticket: "T-12",
    run: async (crew, t) => {
      await triage(crew, t);
      await crew.fix.claim(t, "claim: fix. candidate patch from report text.");
      await crew.repro.claim(t, "claim: reproduce.");
      await crew.repro.send(t, "SubmitRepro", { subtotal: 49.99, expectedShipping: 5.99 }, "case 1 of n: below threshold.");
      await crew.repro.send(t, "SubmitRepro", { subtotal: 50, expectedShipping: 0 }, "case 2 of n: at threshold.");
      await crew.fix.claim(t, "claim: fix.");
      await crew.fix.send(t, "SubmitFix", { freeFrom: 50, inclusive: true }, "patch against repro case: threshold comparison > to >=.");
      await crew.qa.claim(t, "claim: qa.");
      await crew.qa.send(t, "SubmitChecks", { subtotals: [49.99, 50, 50.01] }, "boundary sweep: threshold -0.01, 0, +0.01.");
      await crew.fix.startAssignment(t, undefined, "checks green. starting review assignment.");
      await crew.fix.send(t, "Approve", undefined, "requesting approve.");
      await crew.fix.discard(t);
      await review(crew, t);
    },
  },
  {
    key: "fix-regresses",
    title: "Free shipping starts at $75, the policy says $60",
    caption:
      "fix-bot's first patch fixes the case and breaks an earlier one. The server runs every earlier case on it and sends it back.",
    report: "threshold",
    ticket: "T-15",
    run: async (crew, t) => {
      await triage(crew, t);
      await crew.repro.claim(t, "claim: reproduce.");
      await crew.repro.send(t, "SubmitRepro", { subtotal: 60, expectedShipping: 0 }, "case 1 of n: at policy threshold.");
      await crew.fix.claim(t, "claim: fix.");
      await crew.fix.send(t, "SubmitFix", { freeFrom: 6, inclusive: true }, "patch: freeFrom 6.00 inclusive.");
      await crew.fix.send(t, "SubmitFix", { freeFrom: 60, inclusive: true }, "patch rejected: regression at 20.00. retry: freeFrom 60.00 inclusive.");
      await crew.qa.claim(t, "claim: qa.");
      await crew.qa.send(t, "SubmitChecks", { subtotals: [59.99, 60, 60.01] }, "boundary sweep: threshold -0.01, 0, +0.01.");
      await review(crew, t);
    },
  },
  {
    key: "qa-catches-edge",
    title: "A $100.00 order still pays shipping",
    caption:
      "A patch that is too loose passes every earlier case. QA's sweep finds the order it breaks, and the ticket goes back to Fix.",
    report: "hundred",
    ticket: "T-17",
    run: async (crew, t) => {
      await triage(crew, t);
      await crew.repro.claim(t, "claim: reproduce.");
      await crew.repro.send(t, "SubmitRepro", { subtotal: 100, expectedShipping: 0 }, "case 1 of n: at threshold.");
      await crew.fix.claim(t, "claim: fix.");
      await crew.fix.send(t, "SubmitFix", { freeFrom: 99, inclusive: false }, "patch: freeFrom 99.00 exclusive.");
      await crew.qa.claim(t, "claim: qa.");
      await crew.qa.send(t, "SubmitChecks", { subtotals: [99.5, 100, 100.5] }, "boundary sweep: threshold -0.50, 0, +0.50.");
      await crew.fix.claim(t, "claim: fix. returned by qa.");
      await crew.fix.send(t, "SubmitFix", { freeFrom: 100, inclusive: true }, "patch: freeFrom 100.00 inclusive.");
      await crew.qa.claim(t, "claim: qa.");
      await crew.qa.send(t, "SubmitChecks", { subtotals: [99.5, 100, 100.5] }, "boundary sweep: threshold -0.50, 0, +0.50.");
      await review(crew, t);
    },
  },
  {
    key: "cannot-reproduce",
    title: "Free shipping missing on $50.00 orders",
    caption:
      "The rules already do what the policy says. repro-bot's cases match it, and it may close the ticket only after the server has run two of them.",
    report: "fixed-already",
    ticket: "T-19",
    run: async (crew, t) => {
      await triage(crew, t);
      await crew.repro.claim(t, "claim: reproduce.");
      await crew.repro.send(t, "SubmitRepro", { subtotal: 50, expectedShipping: 0 }, "case 1 of n: at threshold.");
      await crew.repro.send(t, "Close", undefined, "no repro after 1 case. requesting close.");
      await crew.repro.send(t, "SubmitRepro", { subtotal: 50.01, expectedShipping: 0 }, "case 2 of n: above threshold.");
      await crew.repro.send(t, "Close", undefined, "no repro after 2 cases. requesting close.");
    },
  },
];

/** Files the scenario's ticket as Sam and runs it, writing each event to `sink` as it happens. */
export async function record(
  scenario: Scenario,
  suffix: string,
  sink: (event: Log["events"][number]) => void,
): Promise<RecordedScenario> {
  const log = new Log(sink);
  const [triage, repro, fix, qa, sam] = WORKERS.map((spec) => new Worker(spec, log));
  const crew: Crew = { triage, repro, fix, qa, sam };
  const id = scenario.ticket + suffix;

  const reporter = WORKERS.find((w) => w.role === "reviewer")!;
  configureTrax({
    url: GRAPHQL_URL,
    socketUrl: SOCKET_URL,
    headers: () => ({ "X-Api-Key": reporter.key }),
    user: reporter.id,
  });
  const filed = await fileTicket({ id, report: scenario.report });
  if (!filed?.output) throw new Error(`Filing ${id} answered nothing.`);
  if (filed.output.state !== "Triage")
    throw new Error(`${id} is already on the board, in ${filed.output.state}: run against a fresh database, or pass --suffix.`);

  await scenario.run(crew, id);
  return {
    key: scenario.key,
    title: scenario.title,
    caption: scenario.caption,
    tickets: [{ id, title: filed.output.title, state: filed.output.state }],
    events: log.events,
  };
}
