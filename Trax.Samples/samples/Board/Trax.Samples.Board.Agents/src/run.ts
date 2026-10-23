// Runs the scripted scenarios against a running Board host.
//
//   npm run scenario                         every scenario, a line per event
//   npm run scenario -- shipping-boundary    one
//   npm run scenario -- --suffix -2          again on a database that has them (ticket ids T-12-2, ...)
//   npm run scenario -- --out run.json       also write every scenario's events, for the recorder
//
// BOARD_HOST (default localhost:5290) names the host. A host that is not this machine is reached over https.

import { writeFileSync } from "node:fs";
import { SCENARIOS, record, type RecordedScenario } from "./scenarios";

const args = process.argv.slice(2);
const option = (name: string): string | undefined => {
  const at = args.indexOf(name);
  if (at < 0) return undefined;
  const value = args[at + 1];
  args.splice(at, 2);
  return value;
};
const out = option("--out");
const suffix = option("--suffix") ?? "";
const chosen = args.length ? SCENARIOS.filter((s) => args.includes(s.key)) : SCENARIOS;
if (args.length && chosen.length !== args.length)
  throw new Error(`Unknown scenario; the scenarios are ${SCENARIOS.map((s) => s.key).join(", ")}.`);

const recorded: RecordedScenario[] = [];
for (const scenario of chosen) {
  console.log(`# ${scenario.key}: ${scenario.title}`);
  recorded.push(
    await record(scenario, suffix, (e) => {
      const what =
        e.say ??
        (e.claim?.problem && `${e.claim.problem.code}: ${e.claim.problem.message}`) ??
        (e.answer?.problem && `${e.answer.problem.code}: ${e.answer.problem.message}`) ??
        (e.answer && `-> ${e.answer.state}`) ??
        (e.check && `${e.check.train} ${e.check.status}${e.check.message ? `: ${e.check.message}` : ""}`) ??
        (e.ticket && `${e.ticket.id} ${e.ticket.state}${e.ticket.note ? `: ${e.ticket.note}` : ""}`) ??
        (e.package && `package: ${e.package.instructions}`) ??
        "";
      console.log(`${String(e.t).padStart(6)} ${String(e.move).padStart(2)} ${e.kind.padEnd(7)} ${e.agent.padEnd(10)} ${what}`);
    }),
  );
}
if (out) writeFileSync(out, JSON.stringify(recorded, null, 2) + "\n");
process.exit(0);
