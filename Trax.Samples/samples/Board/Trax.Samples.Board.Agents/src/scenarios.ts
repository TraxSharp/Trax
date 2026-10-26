// The scripted runs: five tickets, each worked through the lanes by the same five clients, and each showing one thing.
// The agents are scripted (what each sends, and a caption saying what each move means, is written here), but everything
// they send goes to the real host through the generated client, and every answer, refusal and check result is the host's. Each move names
// the answer it expects, and a host that answers anything else stops the run.

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

// Triage and review are the same in every scenario but the first, so they are told in a line.
async function triage(crew: Crew, t: string, caption = "triage-bot files the ticket under shipping, and the server moves it to Reproduce."): Promise<void> {
  await crew.triage.take(t, "Triage", { area: "shipping" }, { say: "area: shipping.", caption, expect: "Accepted", ticket: "Reproducing" });
}

async function review(crew: Crew, t: string, caption = "Sam approves the fix, and the server marks the ticket done."): Promise<void> {
  await crew.sam.take(t, "Approve", undefined, { say: "Approved.", caption, expect: "Accepted", ticket: "Done" });
}

export const SCENARIOS: Scenario[] = [
  {
    key: "every-lane",
    title: "A ticket goes through every lane",
    caption:
      "A bug report goes through Triage, Reproduce, Fix, QA and Review, each worked by its own client. None of them moves the ticket: the server does, once it has checked their work.",
    report: "boundary",
    ticket: "T-12",
    run: async (crew, t) => {
      await triage(crew, t, "triage-bot takes the new ticket and says it belongs to shipping. The server checks that, and moves the ticket to Reproduce.");
      await crew.repro.take(t, "SubmitRepro", { subtotal: 50, expectedShipping: 0 }, {
        say: "repro: $50.00 order. policy: ships free.",
        caption: "repro-bot sends a case: a $50.00 order, which the policy says ships free. The server runs it on the shop's rules, gets $5.99, and moves the ticket to Fix.",
        expect: "Accepted",
        ticket: "ReadyToFix",
      });
      await crew.fix.take(t, "SubmitFix", { freeFrom: 50, inclusive: true }, {
        say: "patch: free from $50.00, $50.00 included.",
        caption: "fix-bot sends a patch: free shipping from $50.00. The server runs the repro case and four cases from earlier fixes on it. All pass, so the ticket goes to QA.",
        expect: "Accepted",
        ticket: "InQA",
      });
      await crew.qa.take(t, "SubmitChecks", { subtotals: [49.99, 50, 50.01] }, {
        say: "check: $49.99, $50.00, $50.01.",
        caption: "qa-bot has the server try $49.99, $50.00 and $50.01 on the patched rules. All three charge what the policy says, so the ticket goes to Review.",
        expect: "Accepted",
        ticket: "InReview",
      });
      await review(crew, t, "Sam, the one person on the board, approves the fix, and the server marks the ticket done.");
    },
  },
  {
    key: "skip-ahead",
    title: "An agent tries to skip ahead",
    caption:
      "fix-bot asks for the ticket before it reaches Fix, then tries to approve its own fix. The server refuses both, and the ticket carries on as normal.",
    report: "forty",
    ticket: "T-13",
    run: async (crew, t) => {
      await triage(crew, t);
      await crew.fix.claim(t, {
        say: "claim: fix.",
        caption: "fix-bot asks for the ticket before anyone has reproduced the bug. The ticket is not in Fix, so the server has nothing to give it.",
        expect: "NOTHING_TO_CLAIM",
      });
      await crew.repro.take(t, "SubmitRepro", { subtotal: 40, expectedShipping: 0 }, {
        say: "repro: $40.00 order. policy: ships free.",
        caption: "repro-bot shows the bug: a $40.00 order pays $3.99, and the policy says it ships free. The ticket moves to Fix.",
        expect: "Accepted",
        ticket: "ReadyToFix",
      });
      await crew.fix.take(t, "SubmitFix", { freeFrom: 40, inclusive: true }, {
        say: "patch: free from $40.00, $40.00 included.",
        caption: "Now fix-bot can claim it. Its patch passes the repro case and the cases from earlier fixes, and the ticket goes to QA.",
        expect: "Accepted",
        ticket: "InQA",
      });
      await crew.qa.take(t, "SubmitChecks", { subtotals: [39.99, 40, 40.01] }, {
        say: "check: $39.99, $40.00, $40.01.",
        caption: "qa-bot's three orders around $40 all charge what the policy says. The ticket goes to Review.",
        expect: "Accepted",
        ticket: "InReview",
      });
      await crew.fix.sendUnclaimed(t, "Approve", undefined, {
        say: "approve.",
        caption: "fix-bot tries to approve its own fix. Only a reviewer may send Approve, so the server refuses it, and the ticket stays in Review.",
        expect: "FORBIDDEN",
        ticket: "InReview",
      });
      await review(crew, t);
    },
  },
  {
    key: "fix-regresses",
    title: "A fix that breaks an earlier case",
    caption:
      "fix-bot's first patch has a typo. It fixes the reported order but breaks a case an earlier fix was checked against, so the server rejects it.",
    report: "threshold",
    ticket: "T-15",
    run: async (crew, t) => {
      await triage(crew, t);
      await crew.repro.take(t, "SubmitRepro", { subtotal: 60, expectedShipping: 0 }, {
        say: "repro: $60.00 order. policy: ships free.",
        caption: "repro-bot shows the bug: a $60.00 order pays $4.99, and the policy says it ships free. The ticket moves to Fix.",
        expect: "Accepted",
        ticket: "ReadyToFix",
      });
      await crew.fix.take(t, "SubmitFix", { freeFrom: 6, inclusive: true }, {
        say: "patch: free from $6.00.",
        caption: "fix-bot sends free from $6.00, a typo for $60. The $60.00 order passes, but an earlier fix's $20.00 case would now ship free. The server rejects the patch.",
        expect: "Rejected",
        ticket: "ReadyToFix",
      });
      await crew.fix.send(t, "SubmitFix", { freeFrom: 60, inclusive: true }, {
        say: "patch: free from $60.00, $60.00 included.",
        caption: "fix-bot sends free from $60.00. The repro case and all three earlier cases pass, and the ticket goes to QA.",
        expect: "Accepted",
        ticket: "InQA",
      });
      await crew.qa.take(t, "SubmitChecks", { subtotals: [59.99, 60, 60.01] }, {
        say: "check: $59.99, $60.00, $60.01.",
        caption: "qa-bot's three orders around $60 all charge what the policy says. The ticket goes to Review.",
        expect: "Accepted",
        ticket: "InReview",
      });
      await review(crew, t);
    },
  },
  {
    key: "qa-catches-edge",
    title: "QA finds what the fix missed",
    caption:
      "fix-bot's patch passes every case the server has. qa-bot tries orders around the threshold, finds one the patch gets wrong, and the ticket goes back to Fix.",
    report: "hundred",
    ticket: "T-17",
    run: async (crew, t) => {
      await triage(crew, t);
      await crew.repro.take(t, "SubmitRepro", { subtotal: 100, expectedShipping: 0 }, {
        say: "repro: $100.00 order. policy: ships free.",
        caption: "repro-bot shows the bug: a $100.00 order pays $7.50, and the policy says it ships free. The ticket moves to Fix.",
        expect: "Accepted",
        ticket: "ReadyToFix",
      });
      await crew.fix.take(t, "SubmitFix", { freeFrom: 99, inclusive: false }, {
        say: "patch: free above $99.00.",
        caption: "fix-bot sends free above $99.00. That gets $100.00 right, and the cases from earlier fixes pass too, so the server sends the ticket to QA.",
        expect: "Accepted",
        ticket: "InQA",
      });
      await crew.qa.take(t, "SubmitChecks", { subtotals: [99.5, 100, 100.5] }, {
        say: "check: $99.50, $100.00, $100.50.",
        caption: "qa-bot tries $99.50. The patch ships it free, but the policy charges $7.50 below $100. QA did its job, and the server sends the ticket back to Fix.",
        expect: "Accepted",
        ticket: "ReadyToFix",
      });
      await crew.fix.take(t, "SubmitFix", { freeFrom: 100, inclusive: true }, {
        say: "patch: free from $100.00, $100.00 included.",
        caption: "fix-bot claims the ticket again and sends free from $100.00. Every case passes, and the ticket goes back to QA.",
        expect: "Accepted",
        ticket: "InQA",
      });
      await crew.qa.take(t, "SubmitChecks", { subtotals: [99.5, 100, 100.5] }, {
        say: "check: $99.50, $100.00, $100.50.",
        caption: "This time all three of qa-bot's orders charge what the policy says. The ticket goes to Review.",
        expect: "Accepted",
        ticket: "InReview",
      });
      await review(crew, t);
    },
  },
  {
    key: "cannot-reproduce",
    title: "A bug that does not reproduce",
    caption:
      "The shop's rules already match the policy. repro-bot may close the ticket, but only once the server has run two of its cases and neither showed the bug.",
    report: "fixed-already",
    ticket: "T-19",
    run: async (crew, t) => {
      await triage(crew, t);
      await crew.repro.take(t, "SubmitRepro", { subtotal: 75, expectedShipping: 0 }, {
        say: "repro: $75.00 order. policy: ships free.",
        caption: "repro-bot tries the reported order, $75.00. The rules ship it free, as the policy says. The bug does not reproduce, and the ticket stays in Reproduce.",
        expect: "Rejected",
        ticket: "Reproducing",
      });
      await crew.repro.send(t, "Close", undefined, {
        say: "close: cannot reproduce.",
        caption: "repro-bot asks to close the ticket. The server refuses: a close needs two cases that did not reproduce, and it has run one.",
        expect: "GUARD_FAILED",
        ticket: "Reproducing",
      });
      await crew.repro.send(t, "SubmitRepro", { subtotal: 50, expectedShipping: 0 }, {
        say: "repro: $50.00 order. policy: ships free.",
        caption: "repro-bot tries the edge of the policy, $50.00. It ships free too, as it should. That is two cases.",
        expect: "Rejected",
        ticket: "Reproducing",
      });
      await crew.repro.send(t, "Close", undefined, {
        say: "close: cannot reproduce.",
        caption: "repro-bot asks again. The server counts two cases it ran itself, closes the ticket, and moves it to Done.",
        expect: "Closed",
        ticket: "Closed",
      });
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
