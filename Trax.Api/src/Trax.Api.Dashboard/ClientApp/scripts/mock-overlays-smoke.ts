// Exercises the Phase 2 overlays end-to-end over the captured fixtures: each mutation writes
// the store delta, and a follow-up query (list + detail) reflects it. Run:
//   npx tsx scripts/mock-overlays-smoke.ts
import { createMockClient } from "../src/mock/client";
import { devMockSeed } from "../src/mock/seeds";
import { createMockStore } from "../src/mock/store/mock-store";
import {
  acknowledgeDeadLetterDocument,
  cancelMachineInstanceDocument,
  deadLetterDetailDocument,
  deadLettersDocument,
  executionsDocument,
  machineInstanceDocument,
  machineInstancesAttentionDocument,
  manifestDetailDocument,
  manifestGroupsDocument,
  manifestsDocument,
  resumeExecutionDocument,
  runGraphDocument,
  schedulerConfigDocument,
  updateManifestDocument,
  updateManifestGroupDocument,
  updateSchedulerDocument,
  workQueueDetailDocument,
} from "../src/trax/index.g";

// As `dev:mock` builds it: the fixtures, with the seed for what they do not hold.
const client = createMockClient({ store: createMockStore({ exposeOnWindow: false }), overrides: devMockSeed });
const get = (o: unknown, path: string) =>
  path.split(".").reduce<unknown>((a, k) => (a as Record<string, unknown>)?.[k], o);

const results: string[] = [];
function check(label: string, cond: boolean) {
  results.push(`${cond ? "PASS" : "FAIL"}  ${label}`);
}

// ── Dead letters: acknowledge first row -> list + detail show ACKNOWLEDGED + note ──
{
  const list = await client.query(deadLettersDocument, { take: 25 });
  const first = (get(list.data, "operations.deadLetters.deadLetters.items") as { id: number }[])[0];
  await client.mutation(acknowledgeDeadLetterDocument, { id: first.id, note: "handled offline" });

  const after = await client.query(deadLettersDocument, { take: 25 });
  const row = (get(after.data, "operations.deadLetters.deadLetters.items") as { id: number; status: string; resolutionNote: string }[]).find((r) => r.id === first.id)!;
  check("deadLetters list row -> ACKNOWLEDGED + note", row.status === "ACKNOWLEDGED" && row.resolutionNote === "handled offline");

  const detail = await client.query(deadLetterDetailDocument, { id: first.id });
  check("deadLetter detail -> ACKNOWLEDGED", get(detail.data, "operations.deadLetters.deadLetter.status") === "ACKNOWLEDGED");
}

// ── Manifests: update first row -> list + detail reflect the patch ──
{
  const list = await client.query(manifestsDocument, { take: 25 });
  const first = (get(list.data, "operations.manifests.items") as { id: number }[])[0];
  await client.mutation(updateManifestDocument, { id: first.id, input: { priority: 99, isEnabled: false, maxRetries: 8 } });

  const after = await client.query(manifestsDocument, { take: 25 });
  const row = (get(after.data, "operations.manifests.items") as { id: number; priority: number; isEnabled: boolean; maxRetries: number }[]).find((r) => r.id === first.id)!;
  check("manifests list row -> patched", row.priority === 99 && row.isEnabled === false && row.maxRetries === 8);

  const detail = await client.query(manifestDetailDocument, { id: first.id });
  check("manifest detail -> patched", get(detail.data, "operations.manifestDetail.priority") === 99);
}

// ── Manifest groups: update first group -> list reflects it ──
{
  const list = await client.query(manifestGroupsDocument, { take: 25 });
  const first = (get(list.data, "operations.manifestGroups.groups.items") as { id: number }[])[0];
  await client.mutation(updateManifestGroupDocument, { id: first.id, input: { priority: 7, isEnabled: false } });

  const after = await client.query(manifestGroupsDocument, { take: 25 });
  const row = (get(after.data, "operations.manifestGroups.groups.items") as { id: number; priority: number; isEnabled: boolean }[]).find((r) => r.id === first.id)!;
  check("manifestGroups list row -> patched", row.priority === 7 && row.isEnabled === false);
}

// ── Scheduler config: update -> config reflects it ──
{
  await client.mutation(updateSchedulerDocument, { input: { maxActiveJobs: 42, defaultMaxRetries: 9 } });
  const after = await client.query(schedulerConfigDocument, {});
  const cfg = get(after.data, "operations.config.scheduler") as { maxActiveJobs: number; defaultMaxRetries: number };
  check("schedulerConfig -> patched", cfg.maxActiveJobs === 42 && cfg.defaultMaxRetries === 9);
}

// ── Resume: a run that stopped after its checkpoint offers a resume (a captured completed run does
// not); resuming queues an entry, and a second is refused while it is queued ──
{
  const list = await client.query(executionsDocument, { take: 25 });
  const completed = (get(list.data, "operations.executions.items") as { id: number }[])[0];
  const done = await client.query(runGraphDocument, { metadataId: completed.id });
  check("runGraph of a captured completed run -> not resumable", get(done.data, "operations.runGraph.canResume") === false);
  const flat = get(done.data, "operations.runGraph.allNodes") as { id: string; parentId: string | null; depth: number }[];
  check(
    "runGraph -> every node in allNodes, with where it sits",
    flat.some((n) => n.parentId === "Signals#4" && n.depth === 1) && flat.some((n) => n.parentId == null && n.depth === 0),
  );
  const failed = { id: 424_242 };
  const graph = await client.query(runGraphDocument, { metadataId: failed.id });
  check("runGraph of a stopped run -> resumable", get(graph.data, "operations.runGraph.canResume") === true);
  const ack = await client.mutation(resumeExecutionDocument, { id: failed.id, from: null });
  const entryId = get(ack.data, "operations.resumeExecution.id") as number;
  const entry = await client.query(workQueueDetailDocument, { id: entryId });
  check("resumeExecution -> queued entry", get(entry.data, "operations.workQueue.detail.status") === "QUEUED");
  const again = await client.mutation(resumeExecutionDocument, { id: failed.id, from: null });
  check("resumeExecution again -> refused", get(again.data, "operations.resumeExecution.success") === false);
}

// ── State machines: cancel a system instance's live run -> its run shows the cancel request ──
{
  const list = await client.query(machineInstancesAttentionDocument, { skip: 0, take: 20, ownerKind: "SYSTEM" });
  const listed = get(list.data, "operations.machineInstances.items") as { machine: string; id: string }[];
  // The list does not say which instance waits on a live run; each one's detail does.
  let live: { machine: string; id: string } | undefined;
  for (const i of listed) {
    const read = await client.query(machineInstanceDocument, { machine: i.machine, ownerKind: "SYSTEM", id: i.id, rowId: null });
    if (get(read.data, "operations.machineInstance.hasLiveInvokedRun") === true) {
      live = i;
      break;
    }
  }
  if (!live) throw new Error("No listed system instance waits on a live run.");
  const vars = { machine: live.machine, ownerKind: "SYSTEM" as const, id: live.id, rowId: null };
  const ack = await client.mutation(cancelMachineInstanceDocument, { machine: live.machine, ownerKind: "SYSTEM", id: live.id });
  check("cancelMachineInstance -> accepted", get(ack.data, "operations.cancelMachineInstance.success") === true);
  const after = await client.query(machineInstanceDocument, vars);
  const runs = get(after.data, "operations.machineInstance.invokedRuns") as { isLive: boolean; cancellationRequested: boolean }[];
  check("machineInstance -> live run cancel requested", runs.some((r) => r.isLive && r.cancellationRequested));
}

console.log(results.join("\n"));
process.exit(results.every((r) => r.startsWith("PASS")) ? 0 : 1);
