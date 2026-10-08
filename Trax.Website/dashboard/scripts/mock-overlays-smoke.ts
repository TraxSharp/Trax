// Exercises the Phase 2 overlays end-to-end over the captured fixtures: each mutation writes
// the store delta, and a follow-up query (list + detail) reflects it. Run:
//   npx tsx scripts/mock-overlays-smoke.ts
import { createMockClient } from "../src/mock/client";
import { devMockSeed } from "../src/mock/seeds";
import { createMockStore } from "../src/mock/store/mock-store";
import {
  DEAD_LETTERS,
  DEAD_LETTER_DETAIL,
  EXECUTIONS,
  MANIFESTS,
  MANIFEST_DETAIL,
  MANIFEST_GROUPS,
  RUN_GRAPH,
  SCHEDULER_CONFIG,
  WORK_QUEUE_DETAIL,
} from "../src/graphql/queries";
import {
  ACKNOWLEDGE_DEAD_LETTER,
  RESUME_EXECUTION,
  UPDATE_MANIFEST,
  UPDATE_MANIFEST_GROUP,
  UPDATE_SCHEDULER,
} from "../src/graphql/mutations";

// As `dev:mock` builds it: the fixtures, with the seed for what they do not hold.
const client = createMockClient({ store: createMockStore({ exposeOnWindow: false }), overrides: devMockSeed });
const NET = { requestPolicy: "network-only" as const };
const get = (o: unknown, path: string) =>
  path.split(".").reduce<unknown>((a, k) => (a as Record<string, unknown>)?.[k], o);

const results: string[] = [];
function check(label: string, cond: boolean) {
  results.push(`${cond ? "PASS" : "FAIL"}  ${label}`);
}

// ── Dead letters: acknowledge first row -> list + detail show ACKNOWLEDGED + note ──
{
  const list = await client.query(DEAD_LETTERS, { take: 25 }, NET).toPromise();
  const first = (get(list.data, "operations.deadLetters.deadLetters.items") as { id: number }[])[0];
  await client.mutation(ACKNOWLEDGE_DEAD_LETTER, { id: first.id, note: "handled offline" }).toPromise();

  const after = await client.query(DEAD_LETTERS, { take: 25 }, NET).toPromise();
  const row = (get(after.data, "operations.deadLetters.deadLetters.items") as { id: number; status: string; resolutionNote: string }[]).find((r) => r.id === first.id)!;
  check("deadLetters list row -> ACKNOWLEDGED + note", row.status === "ACKNOWLEDGED" && row.resolutionNote === "handled offline");

  const detail = await client.query(DEAD_LETTER_DETAIL, { id: first.id }, NET).toPromise();
  check("deadLetter detail -> ACKNOWLEDGED", get(detail.data, "operations.deadLetters.deadLetter.status") === "ACKNOWLEDGED");
}

// ── Manifests: update first row -> list + detail reflect the patch ──
{
  const list = await client.query(MANIFESTS, { take: 25 }, NET).toPromise();
  const first = (get(list.data, "operations.manifests.items") as { id: number }[])[0];
  await client.mutation(UPDATE_MANIFEST, { id: first.id, input: { priority: 99, isEnabled: false, maxRetries: 8 } }).toPromise();

  const after = await client.query(MANIFESTS, { take: 25 }, NET).toPromise();
  const row = (get(after.data, "operations.manifests.items") as { id: number; priority: number; isEnabled: boolean; maxRetries: number }[]).find((r) => r.id === first.id)!;
  check("manifests list row -> patched", row.priority === 99 && row.isEnabled === false && row.maxRetries === 8);

  const detail = await client.query(MANIFEST_DETAIL, { id: first.id }, NET).toPromise();
  check("manifest detail -> patched", get(detail.data, "operations.manifestDetail.priority") === 99);
}

// ── Manifest groups: update first group -> list reflects it ──
{
  const list = await client.query(MANIFEST_GROUPS, { take: 25 }, NET).toPromise();
  const first = (get(list.data, "operations.manifestGroups.groups.items") as { id: number }[])[0];
  await client.mutation(UPDATE_MANIFEST_GROUP, { id: first.id, input: { priority: 7, isEnabled: false } }).toPromise();

  const after = await client.query(MANIFEST_GROUPS, { take: 25 }, NET).toPromise();
  const row = (get(after.data, "operations.manifestGroups.groups.items") as { id: number; priority: number; isEnabled: boolean }[]).find((r) => r.id === first.id)!;
  check("manifestGroups list row -> patched", row.priority === 7 && row.isEnabled === false);
}

// ── Scheduler config: update -> config reflects it ──
{
  await client.mutation(UPDATE_SCHEDULER, { input: { maxActiveJobs: 42, defaultMaxRetries: 9 } }).toPromise();
  const after = await client.query(SCHEDULER_CONFIG, {}, NET).toPromise();
  const cfg = get(after.data, "operations.config.scheduler") as { maxActiveJobs: number; defaultMaxRetries: number };
  check("schedulerConfig -> patched", cfg.maxActiveJobs === 42 && cfg.defaultMaxRetries === 9);
}

// ── Resume: a run that stopped after its checkpoint offers a resume (a captured completed run does
// not); resuming queues an entry, and a second is refused while it is queued ──
{
  const list = await client.query(EXECUTIONS, { take: 25 }, NET).toPromise();
  const completed = (get(list.data, "operations.executions.items") as { id: number }[])[0];
  const done = await client.query(RUN_GRAPH, { metadataId: completed.id }, NET).toPromise();
  check("runGraph of a captured completed run -> not resumable", get(done.data, "operations.runGraph.canResume") === false);
  const failed = { id: 424_242 };
  const graph = await client.query(RUN_GRAPH, { metadataId: failed.id }, NET).toPromise();
  check("runGraph of a stopped run -> resumable", get(graph.data, "operations.runGraph.canResume") === true);
  const ack = await client.mutation(RESUME_EXECUTION, { id: failed.id, from: null }).toPromise();
  const entryId = get(ack.data, "operations.resumeExecution.id") as number;
  const entry = await client.query(WORK_QUEUE_DETAIL, { id: entryId }, NET).toPromise();
  check("resumeExecution -> queued entry", get(entry.data, "operations.workQueue.detail.status") === "QUEUED");
  const again = await client.mutation(RESUME_EXECUTION, { id: failed.id, from: null }).toPromise();
  check("resumeExecution again -> refused", get(again.data, "operations.resumeExecution.success") === false);
}

console.log(results.join("\n"));
process.exit(results.every((r) => r.startsWith("PASS")) ? 0 : 1);
