import { describe, expect, test } from "vitest";
import { createMockClient } from "./client";
import { createMockStore } from "./store/mock-store";
import {
  MACHINE_IDS,
  effectsScenario,
  executionDetailScenario,
  groupScenario,
  machineScenario,
  manifestScenario,
  persistedOperationsScenario,
  schedulerConfigScenario,
} from "./scenarios";
import {
  USER_OWNED_CANCEL_REFUSAL,
  USER_OWNED_RUN_CANCEL_REFUSAL,
  defaultOverlays,
  userDraftRunsOverlay,
} from "./store/overlays";
import type { MockSchemaOverrides } from "./build-mock-schema";
import {
  cancelExecutionDocument,
  cancelExecutionsDocument,
  cancelGroupsDocument,
  cancelMachineInstanceDocument,
  cancelWorkQueueEntriesDocument,
  cancelWorkQueueEntryDocument,
  configureEffectDocument,
  deactivatePersistedOperationDocument,
  effectsDocument,
  executionDetailDocument,
  executionsDocument,
  logLevelsDocument,
  machineInstanceDocument,
  machineInstancesAttentionDocument,
  manifestDetailDocument,
  manifestGroupsDocument,
  manifestsDocument,
  persistedOperationDetailDocument,
  persistedOperationsDocument,
  queueTrainDocument,
  requeueExecutionDocument,
  restorePersistedOperationDocument,
  resumeExecutionDocument,
  runTrainDocument,
  setAllManifestGroupsEnabledDocument,
  setEffectEnabledDocument,
  setLogLevelsDocument,
  setManifestGroupsEnabledDocument,
  setManifestsEnabledDocument,
  setManifestsReplayDecisionsOnRetryDocument,
  triggerGroupsDocument,
  triggerManifestsDocument,
  uploadPersistedOperationDocument,
  workQueueDetailDocument,
  workQueueDocument,
  type SnapshotOwnerKind,
} from "../trax/index.g";

// Read-after-write for the overlays added with the Blazor-parity pages: each mutation writes the
// session delta and the matching read shows it.
function client(overrides?: MockSchemaOverrides) {
  return createMockClient({
    store: createMockStore({ exposeOnWindow: false, persist: false }),
    overrides,
    fixtures: false,
  });
}
const get = (o: unknown, path: string) =>
  path.split(".").reduce<unknown>((a, k) => (a as Record<string, unknown>)?.[k], o);
type Row = Record<string, unknown> & { id: number };

describe("work queue / runs", () => {
  test("queueTrain returns the entry's id, and the list and detail show it", async () => {
    const c = client();
    const ack = await c
      .mutation(queueTrainDocument, { input: { trainName: "Trax.Demo.OrderTrain", inputJson: '{"a":1}', priority: 3 } });
    const id = get(ack.data, "operations.workQueue.queueTrain.id") as number;
    expect(get(ack.data, "operations.workQueue.queueTrain.success")).toBe(true);
    const detail = await c.query(workQueueDetailDocument, { id });
    expect(get(detail.data, "operations.workQueue.detail.trainName")).toBe("Trax.Demo.OrderTrain");
    expect(get(detail.data, "operations.workQueue.detail.priority")).toBe(3);
    const list = await c.query(workQueueDocument, { take: 25 });
    expect((get(list.data, "operations.workQueue.workQueues.items") as Row[])[0].id).toBe(id);
  });

  // Ids count per store, so what a story or test sees does not depend on what ran before it.
  test("every store hands out the same ids from the start, each once", async () => {
    const queue = (c: ReturnType<typeof client>) =>
      c.mutation(queueTrainDocument, { input: { trainName: "T", inputJson: "{}" } }).then((r) => get(r.data, "operations.workQueue.queueTrain.id"));
    const run = (c: ReturnType<typeof client>) =>
      c.mutation(runTrainDocument, { input: { trainName: "T" } }).then((r) => get(r.data, "operations.workQueue.runTrain.id"));
    const first = client();
    const ids = [await queue(first), await queue(first), await run(first), await run(first)];
    expect(new Set(ids).size).toBe(4);
    const second = client();
    expect([await queue(second), await queue(second), await run(second), await run(second)]).toEqual(ids);
  });

  test("a host whose users' drafts started runs refuses their cancel and skips them in bulk, in the API's words", async () => {
    const c = createMockClient({
      store: createMockStore({ exposeOnWindow: false, persist: false }),
      fixtures: false,
      // Runs that start with no cancel requested, so only the overlay can request one.
      overrides: executionDetailScenario,
      overlays: [...defaultOverlays, userDraftRunsOverlay([7110], [7202])],
    });
    const one = get((await c.mutation(cancelExecutionDocument, { id: 7110 })).data, "operations.cancelExecution");
    expect(one).toEqual({ success: false, count: 0, message: USER_OWNED_RUN_CANCEL_REFUSAL });
    const many = get((await c.mutation(cancelExecutionsDocument, { ids: [7110, 7] })).data, "operations.cancelExecutions");
    expect(many).toEqual({
      success: true,
      count: 1,
      message: `Cancellation requested for 1 of 2 execution(s). 1 skipped: ${USER_OWNED_RUN_CANCEL_REFUSAL}`,
    });
    for (const [id, flagged] of [[7110, false], [7, true]] as const) {
      const d = await c.query(executionDetailDocument, { id });
      expect(get(d.data, "operations.executionDetail.cancellationRequested")).toBe(flagged);
    }
    const entry = get((await c.mutation(cancelWorkQueueEntryDocument, { id: 7202 })).data, "operations.workQueue.cancelWorkQueueEntry");
    expect(entry).toEqual({ success: false, message: USER_OWNED_RUN_CANCEL_REFUSAL });
    const entries = get(
      (await c.mutation(cancelWorkQueueEntriesDocument, { ids: [7202, 7203] })).data,
      "operations.workQueue.cancelWorkQueueEntries",
    );
    expect(entries).toEqual({
      success: true,
      count: 1,
      message: `1 of 2 work queue entry(s) cancelled. 1 skipped: ${USER_OWNED_RUN_CANCEL_REFUSAL}`,
    });
  });

  test("queueTrain and runTrain refuse input that is not JSON", async () => {
    const c = client();
    const q = await c.mutation(queueTrainDocument, { input: { trainName: "T", inputJson: "{bad" } });
    expect(get(q.data, "operations.workQueue.queueTrain.success")).toBe(false);
    const r = await c.mutation(runTrainDocument, { input: { trainName: "T", inputJson: "{bad" } });
    expect(get(r.data, "operations.workQueue.runTrain.message")).toMatch(/not valid JSON/);
  });

  test("runTrain returns the run's id, and the execution detail shows that train", async () => {
    const c = client();
    const ack = await c.mutation(runTrainDocument, { input: { trainName: "Trax.Demo.PingTrain" } });
    const id = get(ack.data, "operations.workQueue.runTrain.id") as number;
    const detail = await c.query(executionDetailDocument, { id });
    expect(get(detail.data, "operations.executionDetail.name")).toBe("Trax.Demo.PingTrain");
  });

  test("requeueExecution returns a work queue entry the detail page resolves", async () => {
    const c = client();
    const ack = await c.mutation(requeueExecutionDocument, { id: 5, askAfresh: true });
    const id = get(ack.data, "operations.requeueExecution.id") as number;
    expect(get(ack.data, "operations.requeueExecution.message")).toMatch(/asks afresh/);
    const detail = await c.query(workQueueDetailDocument, { id });
    expect(get(detail.data, "operations.workQueue.detail.status")).toBe("QUEUED");
  });

  test("resumeExecution queues an entry the detail page resolves, and refuses a second while it is queued", async () => {
    const c = client();
    const ack = await c.mutation(resumeExecutionDocument, { id: 902, from: "Summarize#2" });
    expect(get(ack.data, "operations.resumeExecution")).toMatchObject({ success: true, message: null });
    const id = get(ack.data, "operations.resumeExecution.id") as number;
    const detail = await c.query(workQueueDetailDocument, { id });
    expect(get(detail.data, "operations.workQueue.detail.status")).toBe("QUEUED");
    const again = await c.mutation(resumeExecutionDocument, { id: 902, from: null });
    expect(get(again.data, "operations.resumeExecution")).toEqual({
      success: false,
      message: `A resume of execution 902 is already queued (WorkQueue ${id}); a run is resumed once at a time. Nothing was queued.`,
      id: null,
    });
    // Once that entry is cancelled, the run can be resumed again.
    await c.mutation(cancelWorkQueueEntryDocument, { id });
    const third = await c.mutation(resumeExecutionDocument, { id: 902, from: null });
    expect(get(third.data, "operations.resumeExecution.success")).toBe(true);
  });

  test("cancelExecutions flags every selected execution", async () => {
    const c = client();
    const ack = await c.mutation(cancelExecutionsDocument, { ids: [7, 8] });
    expect(get(ack.data, "operations.cancelExecutions.count")).toBe(2);
    for (const id of [7, 8]) {
      const d = await c.query(executionDetailDocument, { id });
      expect(get(d.data, "operations.executionDetail.cancellationRequested")).toBe(true);
    }
  });
});

describe("state machines", () => {
  const instance = (c: ReturnType<typeof client>, id: string, ownerKind: SnapshotOwnerKind = "SYSTEM", rowId: number | null = null) =>
    c.query(machineInstanceDocument, { machine: ownerKind === "SYSTEM" ? "source-partition" : "topic-map", ownerKind, id, rowId });
  const cancel = (c: ReturnType<typeof client>, id: string, ownerKind = "SYSTEM", machine = "source-partition") =>
    c.mutation(cancelMachineInstanceDocument, { machine, ownerKind, id });

  test("cancelling a dispatched run requests its cancel, and the instance and the run show it", async () => {
    const c = client(machineScenario);
    await instance(c, MACHINE_IDS.dispatched);
    const ack = get((await cancel(c, MACHINE_IDS.dispatched)).data, "operations.cancelMachineInstance");
    expect(ack).toEqual({
      success: true,
      outcome: "CANCEL_REQUESTED",
      state: null,
      message:
        `Cancellation requested for the run instance ${MACHINE_IDS.dispatched} of 'source-partition' waits on in 'Ingesting'. ` +
        "The run stops at its next junction, and the instance then moves through the state's OnCancelled edge.",
    });
    const runs = get((await instance(c, MACHINE_IDS.dispatched)).data, "operations.machineInstance.invokedRuns") as {
      id: number;
      cancellationRequested: boolean;
    }[];
    expect(runs.map((r) => [r.id, r.cancellationRequested])).toEqual([
      [7101, true],
      [7090, false],
    ]);
    const run = await c.query(executionDetailDocument, { id: 7101 });
    expect(get(run.data, "operations.executionDetail.cancellationRequested")).toBe(true);
  });

  test("cancelling a queued run cancels its work queue entry before it starts", async () => {
    const c = client(machineScenario);
    await instance(c, MACHINE_IDS.queued);
    const ack = get((await cancel(c, MACHINE_IDS.queued)).data, "operations.cancelMachineInstance");
    expect(ack).toMatchObject({ success: true, outcome: "RUN_CANCELLED" });
    expect(get((await instance(c, MACHINE_IDS.queued)).data, "operations.machineInstance.queuedInvokedRunEntryId")).toBeNull();
    const entry = await c.query(workQueueDetailDocument, { id: 7201 });
    expect(get(entry.data, "operations.workQueue.detail.status")).toBe("CANCELLED");
  });

  test("refuses a user's draft, an instance waiting on no run and one it does not know, changing nothing", async () => {
    const c = client(machineScenario);
    await instance(c, MACHINE_IDS.idle);
    const user = get((await cancel(c, MACHINE_IDS.draftBuilding, "USER", "topic-map")).data, "operations.cancelMachineInstance");
    expect(user).toEqual({ success: false, outcome: "USER_OWNED", state: null, message: USER_OWNED_CANCEL_REFUSAL });
    const idle = get((await cancel(c, MACHINE_IDS.idle)).data, "operations.cancelMachineInstance");
    expect(idle).toMatchObject({
      success: false,
      outcome: "NO_LIVE_RUN",
      message: `Instance ${MACHINE_IDS.idle} of 'source-partition' is in 'Ingested', which waits on no train run: there is nothing to cancel.`,
    });
    const missing = get((await cancel(c, "3f2c1a00-0000-4000-8000-0000000000ff")).data, "operations.cancelMachineInstance");
    expect(missing).toMatchObject({ success: false, outcome: "NOT_FOUND" });
  });

  // The answer does not depend on whether a page read the instance first.
  test("answers a cancel for an instance no read has served", async () => {
    const dispatched = get((await cancel(client(machineScenario), MACHINE_IDS.dispatched)).data, "operations.cancelMachineInstance");
    expect(dispatched).toMatchObject({ success: true, outcome: "CANCEL_REQUESTED" });
    const c = client(machineScenario);
    const queued = get((await cancel(c, MACHINE_IDS.queued)).data, "operations.cancelMachineInstance");
    expect(queued).toMatchObject({ success: true, outcome: "RUN_CANCELLED" });
    const entry = await c.query(workQueueDetailDocument, { id: 7201 });
    expect(get(entry.data, "operations.workQueue.detail.status")).toBe("CANCELLED");
    const idle = get((await cancel(client(machineScenario), MACHINE_IDS.idle)).data, "operations.cancelMachineInstance");
    expect(idle).toMatchObject({ success: false, outcome: "NO_LIVE_RUN" });
  });

  test("no read returns an instance's context", async () => {
    const c = client(machineScenario);
    const list = await c.query(machineInstancesAttentionDocument, { skip: 0, take: 20 });
    const detail = await instance(c, MACHINE_IDS.dispatched);
    expect(JSON.stringify([list.data, detail.data])).not.toMatch(/context/i);
  });
});

describe("manifests and groups", () => {
  test("setManifestsEnabled and setManifestsReplayDecisionsOnRetry patch list and detail", async () => {
    const c = client(manifestScenario);
    await c.mutation(setManifestsEnabledDocument, { ids: [803, 804], enabled: true });
    await c.mutation(setManifestsReplayDecisionsOnRetryDocument, { ids: [801], replay: false });
    const list = await c.query(manifestsDocument, { take: 25 });
    const rows = get(list.data, "operations.manifests.items") as Row[];
    expect(rows.every((r) => r.isEnabled)).toBe(true);
    expect(rows.find((r) => r.id === 801)!.replayDecisionsOnRetry).toBe(false);
    const detail = await c.query(manifestDetailDocument, { id: 801 });
    expect(get(detail.data, "operations.manifestDetail.replayDecisionsOnRetry")).toBe(false);
  });

  test("setManifestGroupsEnabled, then setAllManifestGroupsEnabled overrides it", async () => {
    const c = client(groupScenario);
    await c.mutation(setManifestGroupsEnabledDocument, { ids: [2], enabled: true });
    let rows = get((await c.query(manifestGroupsDocument, { take: 25 })).data, "operations.manifestGroups.groups.items") as Row[];
    expect(rows.find((r) => r.id === 2)!.isEnabled).toBe(true);
    await c.mutation(setAllManifestGroupsEnabledDocument, { enabled: false });
    rows = get((await c.query(manifestGroupsDocument, { take: 25 })).data, "operations.manifestGroups.groups.items") as Row[];
    expect(rows.every((r) => r.isEnabled === false)).toBe(true);
  });
});

describe("effects", () => {
  test("setEffectEnabled flips the effect in the list", async () => {
    const c = client(effectsScenario);
    const name = "Trax.Effect.Provider.Parameter.ParameterEffectProviderFactory";
    await c.mutation(setEffectEnabledDocument, { fullName: name, enabled: true });
    const list = await c.query(effectsDocument, {});
    const row = (get(list.data, "operations.effects") as { fullName: string; enabled: boolean }[]).find(
      (e) => e.fullName === name,
    );
    expect(row!.enabled).toBe(true);
  });
});

describe("persisted operations", () => {
  const detail = (c: ReturnType<typeof client>, id: string, tenantKey: string | null = null) =>
    c.query(persistedOperationDetailDocument, { id, tenantKey, historyTake: 100 });

  test("an upload is listed and resolvable, with an Upsert in its history", async () => {
    const c = client(persistedOperationsScenario);
    const ack = await c
      .mutation(uploadPersistedOperationDocument, {
        input: { id: "new.v1", document: "query New { operations { health { status } } }", description: "fresh" },
      });
    expect(get(ack.data, "persistedOperations.uploadPersistedOperation.success")).toBe(true);
    const list = await c.query(persistedOperationsDocument, { skip: 0, take: 25 });
    const page = get(list.data, "persistedOperations.persistedOperations") as {
      items: { id: string }[];
      totalCount: number;
    };
    expect(page.items[0].id).toBe("new.v1");
    expect(page.totalCount).toBe(4);
    const d = await detail(c, "new.v1");
    expect(get(d.data, "persistedOperations.persistedOperation.operationName")).toBe("New");
    expect(get(d.data, "persistedOperations.persistedOperationHistory.0.changeType")).toBe("Upsert");
  });

  test("a shape-changing edit is refused with both fingerprints unless bypassed", async () => {
    const c = client(persistedOperationsScenario);
    const up = (document: string, bypassShapeDiff = false) =>
      c.mutation(uploadPersistedOperationDocument, { input: { id: "s.v1", document, bypassShapeDiff } });
    await up("query S { operations { health { status } } }");
    const refused = await up("query S { operations { health { status queueDepth } } }");
    const payload = get(refused.data, "persistedOperations.uploadPersistedOperation") as {
      success: boolean;
      errors: { code: string; oldFingerprint: string; newFingerprint: string }[];
    };
    expect(payload.success).toBe(false);
    expect(payload.errors[0].code).toBe("SHAPE_DIFF_VIOLATION");
    expect(payload.errors[0].oldFingerprint).not.toBe(payload.errors[0].newFingerprint);
    const bypassed = await up("query S { operations { health { status queueDepth } } }", true);
    expect(get(bypassed.data, "persistedOperations.uploadPersistedOperation.success")).toBe(true);
  });

  test("an unbalanced document is a parse error with its location", async () => {
    const c = client(persistedOperationsScenario);
    const r = await c
      .mutation(uploadPersistedOperationDocument, { input: { id: "p.v1", document: "query P {" } });
    expect(get(r.data, "persistedOperations.uploadPersistedOperation.errors.0.code")).toBe("PARSE_FAILED");
    expect(get(r.data, "persistedOperations.uploadPersistedOperation.errors.0.locations.0.line")).toBe(1);
  });

  test("deactivate needs a reason, then restore brings the operation back", async () => {
    const c = client(persistedOperationsScenario);
    const blank = await c
      .mutation(deactivatePersistedOperationDocument, { input: { id: "greet.v1", reason: "  " } });
    expect(get(blank.data, "persistedOperations.deactivatePersistedOperation.errors.0.code")).toBe(
      "INVALID_INPUT",
    );
    await c
      .mutation(deactivatePersistedOperationDocument, { input: { id: "greet.v1", reason: "retired" } });
    let d = await detail(c, "greet.v1");
    expect(get(d.data, "persistedOperations.persistedOperation.isActive")).toBe(false);
    expect(get(d.data, "persistedOperations.persistedOperation.deprecationReason")).toBe("retired");
    const list = await c.query(persistedOperationsDocument, { skip: 0, take: 25 });
    const row = (
      get(list.data, "persistedOperations.persistedOperations.items") as { id: string; isActive: boolean }[]
    ).find((r) => r.id === "greet.v1");
    expect(row!.isActive).toBe(false);
    await c.mutation(restorePersistedOperationDocument, { input: { id: "greet.v1" } });
    d = await detail(c, "greet.v1");
    expect(get(d.data, "persistedOperations.persistedOperation.isActive")).toBe(true);
    expect(get(d.data, "persistedOperations.persistedOperationHistory.0.changeType")).toBe("Restore");
  });
});

describe("batch triggers", () => {
  test("triggerManifests queues an entry per manifest, and a second trigger finds it already queued", async () => {
    const c = client(manifestScenario);
    const first = get(
      (await c.mutation(triggerManifestsDocument, { ids: [801, 802, 801] })).data,
      "operations.triggerManifests",
    ) as Record<string, unknown>;
    expect(first).toMatchObject({ success: true, matched: 2, queued: 2, alreadyQueued: 0, skipped: 0, notes: [] });
    const entries = get(
      (await c.query(workQueueDocument, { take: 25, manifestId: 801 })).data,
      "operations.workQueue.workQueues.items",
    ) as Row[];
    expect(entries.filter((e) => e.manifestId === 801 && e.status === "QUEUED")).toHaveLength(1);
    const again = get(
      (await c.mutation(triggerManifestsDocument, { ids: [801], askAfresh: true })).data,
      "operations.triggerManifests",
    ) as Record<string, unknown>;
    expect(again).toMatchObject({ queued: 0, alreadyQueued: 1 });
    expect(again.message).toMatch(/asking afresh\.$/);
  });

  test("a deleted manifest is skipped with a note; an empty batch is refused", async () => {
    const c = client(manifestScenario);
    const noted = get(
      (await c.mutation(triggerManifestsDocument, { ids: [803, 1_000_009] })).data,
      "operations.triggerManifests",
    ) as Record<string, unknown>;
    expect(noted).toMatchObject({ success: true, matched: 1, queued: 1, skipped: 1 });
    expect(noted.notes).toEqual([{ id: 1_000_009, message: "Manifest 1000009 not found." }]);
    const refused = get(
      (await c.mutation(triggerManifestsDocument, { ids: [] })).data,
      "operations.triggerManifests",
    ) as Record<string, unknown>;
    expect(refused).toMatchObject({ success: false, message: "No ids were given.", queued: 0 });
    const tooMany = Array.from({ length: 1001 }, (_, i) => i + 1);
    expect(
      get((await c.mutation(triggerManifestsDocument, { ids: tooMany })).data, "operations.triggerManifests.success"),
    ).toBe(false);
  });

  test("triggerGroups counts each group's manifests, then finds them already queued", async () => {
    const c = client(groupScenario);
    const first = get((await c.mutation(triggerGroupsDocument, { ids: [1, 2] })).data, "operations.triggerGroups");
    expect(first).toMatchObject({ success: true, matched: 2, queued: 4, alreadyQueued: 0 });
    const again = get((await c.mutation(triggerGroupsDocument, { ids: [2, 1_000_002] })).data, "operations.triggerGroups");
    expect(again).toMatchObject({ matched: 1, queued: 0, alreadyQueued: 2, skipped: 1 });
    expect(get((await c.mutation(triggerGroupsDocument, { ids: [] })).data, "operations.triggerGroups.success")).toBe(false);
  });

  test("cancelGroups flags the active runs of a cancelled group's executions", async () => {
    const running = (id: number, trainState: string) => ({
      id,
      externalId: `e-${id}`,
      name: "Trax.Demo.Run",
      trainState,
      startTime: "2026-07-07T11:00:00.000Z",
      endTime: null,
      failureJunction: null,
      failureReason: null,
      manifestId: 801,
      hostName: null,
      failureClass: "UNCLASSIFIED",
      cancellationRequested: false,
      parentId: null,
      currentlyRunningJunction: null,
    });
    const c = client({
      resolvers: () => ({
        OperationsQueries: {
          executions: () => ({
            items: [running(1, "IN_PROGRESS"), running(2, "COMPLETED")],
            totalCount: 2,
            isEstimatedCount: false,
            skip: 0,
            take: 25,
            nextCursor: null,
          }),
        },
      }),
    });
    const ack = get((await c.mutation(cancelGroupsDocument, { ids: [1] })).data, "operations.cancelGroups");
    expect(ack).toMatchObject({ success: true, count: 1 });
    const rows = get(
      (await c.query(executionsDocument, { take: 25, manifestGroupId: 1 })).data,
      "operations.executions.items",
    ) as Row[];
    expect(rows.map((r) => r.cancellationRequested)).toEqual([true, false]);
    const other = get(
      (await c.query(executionsDocument, { take: 25, manifestGroupId: 2 })).data,
      "operations.executions.items",
    ) as Row[];
    expect(other[0].cancellationRequested).toBe(false);
    expect(get((await c.mutation(cancelGroupsDocument, { ids: [] })).data, "operations.cancelGroups.message")).toBe(
      "No ids were given.",
    );
  });
});

describe("effect settings", () => {
  const JSON_EFFECT = "Trax.Effect.Provider.Json.JsonEffectProviderFactory";
  type Field = { name: string; value: string | null; hasValue: boolean };
  const fieldsOf = async (c: ReturnType<typeof client>) =>
    (
      (get((await c.query(effectsDocument, {})).data, "operations.effects") as {
        fullName: string;
        fields: Field[];
        configuration: string | null;
      }[]).find((e) => e.fullName === JSON_EFFECT)!
    );

  test("configureEffect writes the values and the read shows them; a sensitive one only as set", async () => {
    const c = client(effectsScenario);
    await fieldsOf(c);
    const ack = get(
      (
        await c
          .mutation(configureEffectDocument, {
            fullName: JSON_EFFECT,
            values: [
              { name: "MaxBytes", value: "2048" },
              { name: "LogLevel", value: "Debug" },
              { name: "ConnectionString", value: "s3cret" },
            ],
          })
      ).data,
      "operations.configureEffect",
    );
    expect(ack).toMatchObject({ success: true, count: 3, errors: [] });
    const effect = await fieldsOf(c);
    const byName = Object.fromEntries(effect.fields.map((f) => [f.name, f]));
    expect(byName.MaxBytes.value).toBe("2048");
    expect(byName.LogLevel.value).toBe("Debug");
    expect(byName.ConnectionString).toMatchObject({ value: null, hasValue: true });
    expect(effect.configuration).not.toMatch(/s3cret/);
    expect(JSON.parse(effect.configuration!).logLevel).toBe("Debug");
    // A nullable setting cleared reads back as no value.
    await c.mutation(configureEffectDocument, { fullName: JSON_EFFECT, values: [{ name: "MaxBytes", value: "" }] });
    expect(Object.fromEntries((await fieldsOf(c)).fields.map((f) => [f.name, f])).MaxBytes).toMatchObject({
      value: null,
      hasValue: false,
    });
  });

  test("refuses all or nothing, naming each refused setting", async () => {
    const c = client(effectsScenario);
    await fieldsOf(c);
    const res = get(
      (
        await c
          .mutation(configureEffectDocument, {
            fullName: JSON_EFFECT,
            values: [
              { name: "MaxBytes", value: "4096" },
              { name: "WriteOutputs", value: "maybe" },
              { name: "LogLevel", value: "Verbose" },
              { name: "ShouldWrite", value: "x" },
              { name: "Nope", value: "1" },
              { name: "MaxBytes", value: "1" },
            ],
          })
      ).data,
      "operations.configureEffect",
    ) as { success: boolean; errors: { field: string; message: string }[]; message: string };
    expect(res.success).toBe(false);
    expect(res.errors).toEqual([
      { field: "WriteOutputs", message: "'maybe' is not true or false." },
      { field: "LogLevel", message: "'Verbose' is not one of Debug, Information, Warning." },
      { field: "ShouldWrite", message: "This setting is set in code and cannot be changed here." },
      { field: "Nope", message: "No such setting." },
      { field: "MaxBytes", message: "Given more than once." },
    ]);
    expect(res.message).toMatch(/^The configuration was not saved: /);
    expect(Object.fromEntries((await fieldsOf(c)).fields.map((f) => [f.name, f])).MaxBytes.value).toBe("1048576");
    const unknown = get(
      (await c.mutation(configureEffectDocument, { fullName: "X.Y", values: [{ name: "A", value: "1" }] })).data,
      "operations.configureEffect.message",
    );
    expect(unknown).toBe("No effect named 'X.Y' is registered in this process.");
    const none = get(
      (await c.mutation(configureEffectDocument, { fullName: JSON_EFFECT, values: [] })).data,
      "operations.configureEffect.message",
    );
    expect(none).toBe("No settings were given.");
  });
});

describe("log levels", () => {
  type Level = { category: string; level: string; overridden: boolean; configuredLevel: string | null };
  const levels = async (c: ReturnType<typeof client>) =>
    get((await c.query(logLevelsDocument, {})).data, "operations.config.logLevels") as Level[];

  test("setLogLevels sets a configured category, matched ignoring case, and the read shows it overridden", async () => {
    const c = client(schedulerConfigScenario);
    await levels(c);
    const ack = get(
      (await c.mutation(setLogLevelsDocument, { levels: [{ category: "microsoft.aspnetcore", level: "ERROR" }] })).data,
      "operations.config.setLogLevels",
    );
    expect(ack).toMatchObject({ success: true, count: 1, notApplied: [], message: "1 log level(s) set in this process." });
    const row = (await levels(c)).find((l) => l.category === "Microsoft.AspNetCore")!;
    expect(row).toMatchObject({ level: "Error", overridden: true, configuredLevel: "Warning" });
  });

  test("a category the host sets itself is reported as not applied", async () => {
    const c = client(schedulerConfigScenario);
    await levels(c);
    const ack = get(
      (await c.mutation(setLogLevelsDocument, { levels: [{ category: "Microsoft.Hosting.Lifetime", level: "WARNING" }] }))
        .data,
      "operations.config.setLogLevels",
    );
    expect(ack).toMatchObject({ success: true, notApplied: ["Microsoft.Hosting.Lifetime"] });
    expect((await levels(c)).find((l) => l.category === "Microsoft.Hosting.Lifetime")!.level).toBe("Information");
  });

  test("an unconfigured category or an empty list is refused whole", async () => {
    const c = client(schedulerConfigScenario);
    await levels(c);
    const refused = get(
      (
        await c
          .mutation(setLogLevelsDocument, {
            levels: [
              { category: "Default", level: "DEBUG" },
              { category: "Nope", level: "DEBUG" },
            ],
          })
      ).data,
      "operations.config.setLogLevels",
    );
    expect(refused).toMatchObject({ success: false, message: "Not a category configured under Logging:LogLevel: Nope." });
    expect((await levels(c)).find((l) => l.category === "Default")!.level).toBe("Information");
    expect(
      get((await c.mutation(setLogLevelsDocument, { levels: [] })).data, "operations.config.setLogLevels.message"),
    ).toBe("No levels were given.");
  });
});
