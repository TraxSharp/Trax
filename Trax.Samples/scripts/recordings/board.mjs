// Records the Board sample for traxsharp.net, which plays it back as one demo: the board's lanes, each agent's moves
// and what the server answered, and the machine line that decided each move. Start the Board host on a fresh database
// as shipped (persisted documents only: the agents send only their generated client's ids), then run
//
//   (cd ../Trax.Api/src/Trax.Api.Dashboard/ClientApp && npm ci --ignore-scripts)   # a source build embeds the console
//   dotnet run --project samples/Board/Trax.Samples.Board.Api
//   (cd samples/Board/Trax.Samples.Board.Agents && npm ci)
//   node board.mjs [--copy-to <dir>]
//
// The recorder does not drive the host itself. It runs the agents' own scenarios (Trax.Samples.Board.Agents,
// `src/run.ts --out`), which send everything through the client `trax client` generated from the host's manifest,
// each worker with its own key, and writes down what each one sent and what the server answered, with the time it
// arrived. Nothing is invented: every answer, refusal, check verdict and ticket note is the host's. BOARD_HOST
// (default localhost:5290) names the host, as it does for the agents.
//
// Five scenarios, one ticket each, each showing one thing: every-lane (a ticket through every lane, nothing refused),
// skip-ahead (fix-bot is refused a claim in a lane the ticket is not in, and an approval its role cannot make),
// fix-regresses (the server's earlier cases refuse a patch), qa-catches-edge (QA's sweep sends a patch back to Fix) and
// cannot-reproduce (repro-bot may close the ticket only after two cases). Each move carries the script's caption, and
// a move that was a mistake or a finding its mark: the code it points at is copied in as a span of lines (rulesOf).
//
// out/board/run.json holds the scenarios as the agents recorded them, and out/board-recordings.json the website's
// copy (Trax.Website's src/data/board-recordings.json): both machines verbatim, the lines each move lights, the
// lanes, the workers, and the scenarios. The website copy changes nothing the agents recorded. --copy-to <dir> also
// writes it there; --copy-only rewrites it from out/ without recording.

import { execFileSync } from "node:child_process";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const SAMPLE = resolve(here, "../../samples/Board");
const AGENTS = join(SAMPLE, "Trax.Samples.Board.Agents");
const OUT = join(here, "out", "board");
const ASSIGNMENT_FILE = "Trax.Samples.Board/Machines/AssignmentMachine.cs";
const TICKET_FILE = "Trax.Samples.Board/Machines/TicketMachine.cs";
// The trains whose code catches a move the machines do not: a claim out of its lane, and the checks' comparisons.
const RULE_FILES = {
  claim: "Trax.Samples.Board/Trains/Work/ClaimWorkTrain.cs",
  reproduce: "Trax.Samples.Board/Trains/Checks/ReproduceTrain.cs",
  verifyFix: "Trax.Samples.Board/Trains/Checks/VerifyFixTrain.cs",
  qa: "Trax.Samples.Board/Trains/Checks/QaTrain.cs",
};

const LANES = [
  { key: "triage", name: "Triage", states: ["Triage"], worker: "triage-bot" },
  { key: "reproduce", name: "Reproduce", states: ["Reproducing"], worker: "repro-bot" },
  { key: "fix", name: "Fix", states: ["ReadyToFix"], worker: "fix-bot" },
  { key: "qa", name: "QA", states: ["InQA"], worker: "qa-bot" },
  { key: "review", name: "Review", states: ["InReview"], worker: "Sam" },
  { key: "done", name: "Done", states: ["Done", "Closed"], worker: null },
];

const AGENT_LIST = [
  { name: "triage-bot", role: "triage", kind: "agent" },
  { name: "repro-bot", role: "reproducer", kind: "agent" },
  { name: "fix-bot", role: "fixer", kind: "agent" },
  { name: "qa-bot", role: "qa", kind: "agent" },
  { name: "Sam", role: "reviewer", kind: "person" },
];

function source(file) {
  return readFileSync(join(SAMPLE, file), "utf8").replace(/\r/g, "");
}

// The 1-based line of the first line at or after `from` that `test` accepts, or 0.
function find(lines, test, from = 0) {
  for (let i = from; i < lines.length; i++) if (test(lines[i], i)) return i + 1;
  return 0;
}

// The lines a move can light, by the keys the agents write on their events.
function linesOf(assignment, ticket) {
  const a = assignment.split("\n");
  const t = ticket.split("\n");
  const out = {};
  out["assignment:start"] = find(a, (l) => l.includes(".Start<AssignmentStart>("));
  for (const trigger of ["Triage", "SubmitRepro", "Close", "SubmitFix", "SubmitChecks", "Approve", "SendBack"]) {
    const on = find(a, (l) => l.includes(`.On(AssignmentTrigger.${trigger})`));
    const next = find(a, (l) => l.includes(".On(AssignmentTrigger."), on) || a.length + 1;
    out[`assignment:event:${trigger}`] = on;
    const rule = find(a, (l, i) => i + 1 < next && l.includes(".Requires("), on);
    const guard = find(a, (l, i) => i + 1 < next && l.includes(".When("), on);
    if (rule) out[`assignment:rule:${trigger}`] = rule;
    if (guard) out[`assignment:guard:${trigger}`] = guard;
  }
  for (const state of ["CheckingTriage", "CheckingRepro", "CheckingClose", "CheckingFix", "CheckingQa", "CheckingReview"])
    out[`assignment:state:${state}`] = find(a, (l) => l.includes("Checks<") && l.includes(`AssignmentState.${state},`));
  out["assignment:outcome:accepted"] = find(a, (l) => l.trim().startsWith(".OnDone("));
  out["assignment:outcome:rejected"] = find(a, (l) => l.includes(".OnDone(AssignmentState.Rejected"));
  for (const trigger of [
    "Claim", "Release", "Triaged", "NotReproduced", "Reproduced", "CannotReproduce", "FixRejected", "Fixed",
    "QaFailed", "QaPassed", "SentBack", "Approved",
  ])
    out[`ticket:event:${trigger}`] = find(t, (l) => l.includes(`.On(TicketTrigger.${trigger})`));
  for (const state of ["Triage", "Reproducing", "ReadyToFix", "InQA", "InReview", "Done", "Closed"])
    out[`ticket:state:${state}`] = find(
      t,
      (l) => l.includes(`Lane(m, TicketState.${state})`) || l.includes(`m.In(TicketState.${state})`),
    );
  for (const [key, line] of Object.entries(out)) if (!line) throw new Error(`No line for ${key}: the machine changed.`);
  return out;
}

// The code a marked move points at (agent.ts `Mark.rule`), as a span of lines in one source: the page shows the span
// under the board when the move it decided is on screen.
function rulesOf(sources) {
  const span = (source, name, test, count) => {
    const lines = sources[source].split("\n");
    const from = find(lines, test);
    if (!from) throw new Error(`No line for ${name}: ${source} changed.`);
    return { source, from, to: from + count - 1 };
  };
  return {
    "assignment:rule:Approve": span("assignment", "Approve", (l) => l.includes(".On(AssignmentTrigger.Approve)"), 2),
    "assignment:guard:Close": span("assignment", "Close", (l) => l.includes(".On(AssignmentTrigger.Close)"), 4),
    "claim:wrong-lane": span("claim", "the lane check", (l) => l.includes("if (ticket.State != lane.State)"), 6),
    "reproduce:matches": span("reproduce", "the policy match", (l) => l.includes("if (charged == policy)"), 1),
    "verifyFix:broken": span("verifyFix", "the earlier cases", (l) => l.includes("var cases = report"), 5),
    "qa:broken": span("qa", "the sweep", (l) => l.includes("var broken = check"), 3),
  };
}

function websiteCopy() {
  const assignment = source(ASSIGNMENT_FILE);
  const ticket = source(TICKET_FILE);
  const run = JSON.parse(readFileSync(join(OUT, "run.json"), "utf8"));
  const lines = linesOf(assignment, ticket);
  const code = { assignment, ticket };
  for (const [key, file] of Object.entries(RULE_FILES)) code[key] = source(file);
  const rules = rulesOf(code);
  for (const scenario of run.scenarios)
    for (const event of scenario.events) {
      if (event.line && !(event.line in lines)) throw new Error(`${scenario.key} lights ${event.line}, which is not a known line.`);
      if (event.mark && !(event.mark.rule in rules)) throw new Error(`${scenario.key} points at ${event.mark.rule}, which is not a known rule.`);
    }
  const files = { assignment: ASSIGNMENT_FILE, ticket: TICKET_FILE, ...RULE_FILES };
  return {
    recordedAt: run.recordedAt,
    sources: Object.fromEntries(Object.entries(files).map(([key, file]) => [key, { file, code: code[key] }])),
    lines,
    rules,
    lanes: LANES,
    agents: AGENT_LIST,
    scenarios: run.scenarios,
  };
}

function writeCopies(copyTo) {
  const data = JSON.stringify(websiteCopy()) + "\n";
  const local = join(here, "out", "board-recordings.json");
  writeFileSync(local, data);
  console.log(`wrote ${local}`);
  if (copyTo) {
    mkdirSync(copyTo, { recursive: true });
    const target = join(copyTo, "board-recordings.json");
    writeFileSync(target, data);
    console.log(`wrote ${target}`);
  }
}

function main() {
  const args = process.argv.slice(2);
  const copyAt = args.indexOf("--copy-to");
  const copyTo = copyAt >= 0 ? resolve(process.cwd(), args[copyAt + 1]) : null;
  if (args.includes("--copy-only")) return writeCopies(copyTo);

  mkdirSync(OUT, { recursive: true });
  const scenariosFile = join(OUT, "scenarios.json");
  const recordedAt = new Date().toISOString();
  // The agents' own runner, with their generated client: the recorder sends nothing of its own.
  execFileSync("npx", ["tsx", "src/run.ts", "--out", scenariosFile], { cwd: AGENTS, stdio: "inherit" });
  const scenarios = JSON.parse(readFileSync(scenariosFile, "utf8"));
  writeFileSync(join(OUT, "run.json"), JSON.stringify({ recordedAt, scenarios }, null, 2) + "\n");
  for (const s of scenarios) console.log(`recorded ${s.key}: ${s.events.length} events`);
  writeCopies(copyTo);
}

main();
