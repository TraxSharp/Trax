import { beforeAll, describe, expect, test } from "vitest";
import { E2E_URL, gql, items, pick, reachable } from "./helpers";
import {
  DEAD_LETTERS,
  DEAD_LETTER_DETAIL,
  EFFECTS,
  LOG_LEVELS,
  EXECUTIONS,
  EXECUTION_DETAIL,
  MANIFESTS,
  MANIFEST_DETAIL,
  MANIFEST_GROUPS,
  MANIFEST_GROUP_DETAIL,
  PERSISTED_OPERATIONS,
  PERSISTED_OPERATION_DETAIL,
  REQUEUE_ALL_JOB,
  SCHEDULER_CONFIG,
  WORK_QUEUE,
  WORK_QUEUE_DETAIL,
} from "../graphql/queries";
import {
  CANCEL_GROUPS,
  CONFIGURE_EFFECT,
  SET_LOG_LEVELS,
  TRIGGER_GROUPS,
  TRIGGER_MANIFESTS,
  ACKNOWLEDGE_ALL_DEAD_LETTERS,
  ACKNOWLEDGE_DEAD_LETTER,
  CANCEL_EXECUTION,
  CANCEL_EXECUTIONS,
  CANCEL_MANIFEST,
  CANCEL_WORK_QUEUE_ENTRIES,
  CANCEL_WORK_QUEUE_ENTRY,
  DEACTIVATE_PERSISTED_OPERATION,
  QUEUE_TRAIN,
  REQUEUE_ALL_DEAD_LETTERS,
  REQUEUE_DEAD_LETTER,
  REQUEUE_DEAD_LETTERS,
  REQUEUE_EXECUTION,
  RESTORE_PERSISTED_OPERATION,
  RUN_TRAIN,
  SET_ALL_MANIFEST_GROUPS_ENABLED,
  SET_EFFECT_ENABLED,
  SET_MANIFESTS_ENABLED,
  SET_MANIFESTS_REPLAY_DECISIONS_ON_RETRY,
  SET_MANIFEST_GROUPS_ENABLED,
  TRIGGER_MANIFEST,
  TRIGGER_MANIFEST_DELAYED,
  UPDATE_MANIFEST,
  UPDATE_MANIFEST_GROUP,
  UPDATE_SCHEDULER,
  UPLOAD_PERSISTED_OPERATION,
} from "../graphql/mutations";

// Manifest 3: its dead letter is acknowledged (never requeued), so queueing it by trigger cannot
// collide with a requeue on the one-queued-entry-per-manifest index.
const firstManifest = async () =>
  (items(await gql(MANIFESTS, { take: 25 }), "operations.manifests.items") as unknown as {
    id: number;
    externalId: string;
  }[]).find((m) => m.externalId === "e2e-manifest-3")!;

beforeAll(async () => {
  if (!(await reachable()))
    throw new Error(`e2e devhost unreachable at ${E2E_URL}; run \`npm run test:e2e\``);
});

// Entry 6 is the seeded subject-keyed, staged entry the reads check; the cancel flows leave it be.
const STAGED_SUBJECT_ENTRY = 6;
const queuedIds = async () =>
  items(await gql(WORK_QUEUE, { take: 25, status: "QUEUED" }), "operations.workQueue.workQueues.items")
    .map((r) => r.id)
    .filter((id) => id !== STAGED_SUBJECT_ENTRY);
// Dead letter 6 is kept for the ask-afresh flows below.
const ASK_AFRESH_DEAD_LETTER = 6;
const awaitingIds = async () =>
  items(
    await gql(DEAD_LETTERS, { take: 25, status: "AWAITING_INTERVENTION" }),
    "operations.deadLetters.deadLetters.items",
  )
    .map((r) => r.id)
    .filter((id) => id !== ASK_AFRESH_DEAD_LETTER);

// Real mutations against the seeded disposable DB, each verified by re-querying the server.
// Tests run sequentially (fileParallelism: false) and pick a fresh target each time.
describe("writes (real mutations, verified by re-query)", () => {
  test("cancel a queued work-queue entry -> CANCELLED", async () => {
    const [id] = await queuedIds();
    const ack = await gql(CANCEL_WORK_QUEUE_ENTRY, { id });
    expect(pick(ack, "operations.workQueue.cancelWorkQueueEntry.success")).toBe(true);
    const detail = await gql(WORK_QUEUE_DETAIL, { id });
    expect(pick(detail, "operations.workQueue.detail.status")).toBe("CANCELLED");
  });

  test("batch-cancel queued entries -> all CANCELLED", async () => {
    const ids = (await queuedIds()).slice(0, 2);
    expect(ids.length).toBe(2);
    const ack = await gql(CANCEL_WORK_QUEUE_ENTRIES, { ids });
    expect(pick(ack, "operations.workQueue.cancelWorkQueueEntries.count")).toBe(ids.length);
    for (const id of ids) {
      const detail = await gql(WORK_QUEUE_DETAIL, { id });
      expect(pick(detail, "operations.workQueue.detail.status")).toBe("CANCELLED");
    }
  });

  test("acknowledge a dead letter -> ACKNOWLEDGED with the note", async () => {
    const [id] = await awaitingIds();
    await gql(ACKNOWLEDGE_DEAD_LETTER, { id, note: "e2e acknowledgement" });
    const detail = await gql(DEAD_LETTER_DETAIL, { id });
    expect(pick(detail, "operations.deadLetters.deadLetter.status")).toBe("ACKNOWLEDGED");
    expect(pick(detail, "operations.deadLetters.deadLetter.resolutionNote")).toBe("e2e acknowledgement");
  });

  test("requeue a dead letter -> RETRIED", async () => {
    const [id] = await awaitingIds();
    await gql(REQUEUE_DEAD_LETTER, { id });
    const detail = await gql(DEAD_LETTER_DETAIL, { id });
    expect(pick(detail, "operations.deadLetters.deadLetter.status")).toBe("RETRIED");
  });

  test("update a manifest -> patched fields persist", async () => {
    const id = items(await gql(MANIFESTS, { take: 1 }), "operations.manifests.items")[0].id;
    await gql(UPDATE_MANIFEST, { id, input: { priority: 77, isEnabled: false, maxRetries: 8 } });
    const detail = await gql(MANIFEST_DETAIL, { id });
    expect(pick(detail, "operations.manifestDetail.priority")).toBe(77);
    expect(pick(detail, "operations.manifestDetail.isEnabled")).toBe(false);
    expect(pick(detail, "operations.manifestDetail.maxRetries")).toBe(8);
  });

  test("update a manifest group -> patched", async () => {
    const id = items(await gql(MANIFEST_GROUPS, { take: 1 }), "operations.manifestGroups.groups.items")[0].id;
    await gql(UPDATE_MANIFEST_GROUP, { id, input: { priority: 9, isEnabled: false } });
    const detail = await gql(MANIFEST_GROUP_DETAIL, { id });
    expect(pick(detail, "operations.manifestGroups.group.priority")).toBe(9);
    expect(pick(detail, "operations.manifestGroups.group.isEnabled")).toBe(false);
  });

  test("update scheduler config -> patched", async () => {
    await gql(UPDATE_SCHEDULER, { input: { maxActiveJobs: 33 } });
    const cfg = await gql(SCHEDULER_CONFIG, {});
    expect(pick(cfg, "operations.config.scheduler.maxActiveJobs")).toBe(33);
  });

  test("cancel an in-progress execution -> cancellationRequested", async () => {
    const id = items(
      await gql(EXECUTIONS, { take: 25, trainState: "IN_PROGRESS" }),
      "operations.executions.items",
    )[0].id;
    const ack = await gql(CANCEL_EXECUTION, { id });
    expect(pick(ack, "operations.cancelExecution.count")).toBe(1);
    const detail = await gql(EXECUTION_DETAIL, { id });
    expect(pick(detail, "operations.executionDetail.cancellationRequested")).toBe(true);
  });

  test("trigger a manifest with a delay -> accepted", async () => {
    const m = await firstManifest();
    const ack = await gql(TRIGGER_MANIFEST_DELAYED, {
      externalId: m.externalId,
      delay: "PT5M",
    });
    expect(pick(ack, "operations.triggerManifestDelayed.success")).toBe(true);
  });

  test("cancel a manifest's running executions -> response", async () => {
    const m = await firstManifest();
    const ack = await gql(CANCEL_MANIFEST, { externalId: m.externalId });
    expect(typeof pick(ack, "operations.cancelManifest.success")).toBe("boolean");
  });

  test("requeue a dead letter asking afresh -> RETRIED, then its new entry is queued", async () => {
    const ack = await gql(REQUEUE_DEAD_LETTER, { id: ASK_AFRESH_DEAD_LETTER, askAfresh: true });
    const res = pick(ack, "operations.deadLetters.requeueDeadLetter") as { success: boolean; workQueueId: number };
    expect(res.success).toBe(true);
    const detail = await gql(DEAD_LETTER_DETAIL, { id: ASK_AFRESH_DEAD_LETTER });
    expect(pick(detail, "operations.deadLetters.deadLetter.status")).toBe("RETRIED");
    expect(pick(await gql(WORK_QUEUE_DETAIL, { id: res.workQueueId }), "operations.workQueue.detail.status")).toBe(
      "QUEUED",
    );
    // A batch requeue that asks afresh refuses nothing it cannot requeue: the resolved one is skipped.
    const batch = await gql(REQUEUE_DEAD_LETTERS, { ids: [ASK_AFRESH_DEAD_LETTER], askAfresh: true });
    expect(pick(batch, "operations.deadLetters.requeueDeadLetters.count")).toBe(0);
  });

  test("cancel several executions in one call -> each flagged", async () => {
    const ids = items(
      await gql(EXECUTIONS, { take: 25, trainState: "IN_PROGRESS" }),
      "operations.executions.items",
    ).map((r) => r.id);
    expect(ids.length).toBeGreaterThan(0);
    const ack = await gql(CANCEL_EXECUTIONS, { ids });
    expect(pick(ack, "operations.cancelExecutions.success")).toBe(true);
    for (const id of ids)
      expect(pick(await gql(EXECUTION_DETAIL, { id }), "operations.executionDetail.cancellationRequested")).toBe(true);
  });

  test("re-queue a run asking afresh -> a queued entry for its train", async () => {
    const ack = await gql(REQUEUE_EXECUTION, { id: 7, askAfresh: true });
    const res = pick(ack, "operations.requeueExecution") as { success: boolean; id: number };
    expect(res.success).toBe(true);
    const entry = pick(await gql(WORK_QUEUE_DETAIL, { id: res.id }), "operations.workQueue.detail") as Record<
      string,
      unknown
    >;
    expect(entry.trainName).toBe("Trax.Dashboard.DevHost.IBroadcastPingTrain");
    expect(entry.status).toBe("QUEUED");
  });

  test("queue a train -> the returned id is its work queue entry", async () => {
    const ack = await gql(QUEUE_TRAIN, {
      input: { trainName: "Trax.Dashboard.DevHost.IBroadcastPingTrain", inputJson: '{"label":"queued"}', priority: 4 },
    });
    const res = pick(ack, "operations.workQueue.queueTrain") as { success: boolean; id: number };
    expect(res.success).toBe(true);
    const entry = pick(await gql(WORK_QUEUE_DETAIL, { id: res.id }), "operations.workQueue.detail") as Record<
      string,
      unknown
    >;
    expect(entry).toMatchObject({ trainName: "Trax.Dashboard.DevHost.IBroadcastPingTrain", priority: 4 });
    const refused = await gql(QUEUE_TRAIN, { input: { trainName: "Trax.No.Such.Train" } });
    expect(pick(refused, "operations.workQueue.queueTrain.success")).toBe(false);
  });

  test("run a train -> the returned id is its run", async () => {
    const ack = await gql(RUN_TRAIN, {
      input: { trainName: "Trax.Dashboard.DevHost.IBroadcastPingTrain", inputJson: '{"label":"ran"}' },
    });
    const res = pick(ack, "operations.workQueue.runTrain") as { success: boolean; id: number };
    expect(res.success).toBe(true);
    expect(pick(await gql(EXECUTION_DETAIL, { id: res.id }), "operations.executionDetail.name")).toBe(
      "Trax.Dashboard.DevHost.IBroadcastPingTrain",
    );
  });

  test("enable / disable manifests in one call -> each flipped", async () => {
    const off = await gql(SET_MANIFESTS_ENABLED, { ids: [3, 4], enabled: false });
    expect(pick(off, "operations.setManifestsEnabled.success")).toBe(true);
    for (const id of [3, 4])
      expect(pick(await gql(MANIFEST_DETAIL, { id }), "operations.manifestDetail.isEnabled")).toBe(false);
    await gql(SET_MANIFESTS_ENABLED, { ids: [3, 4], enabled: true });
    expect(pick(await gql(MANIFEST_DETAIL, { id: 3 }), "operations.manifestDetail.isEnabled")).toBe(true);
  });

  test("replay decisions on retry -> off then on, read back", async () => {
    await gql(SET_MANIFESTS_REPLAY_DECISIONS_ON_RETRY, { ids: [3], replay: false });
    expect(pick(await gql(MANIFEST_DETAIL, { id: 3 }), "operations.manifestDetail.replayDecisionsOnRetry")).toBe(false);
    const list = items(await gql(MANIFESTS, { take: 25 }), "operations.manifests.items") as unknown as {
      id: number;
      replayDecisionsOnRetry: boolean;
    }[];
    expect(list.find((m) => m.id === 3)!.replayDecisionsOnRetry).toBe(false);
    await gql(SET_MANIFESTS_REPLAY_DECISIONS_ON_RETRY, { ids: [3], replay: true });
    expect(pick(await gql(MANIFEST_DETAIL, { id: 3 }), "operations.manifestDetail.replayDecisionsOnRetry")).toBe(true);
  });

  test("trigger a manifest asking afresh -> accepted", async () => {
    const ack = await gql(TRIGGER_MANIFEST, { externalId: "e2e-manifest-3", askAfresh: true });
    expect(pick(ack, "operations.triggerManifest.success")).toBe(true);
  });

  test("enable / disable selected groups, then all groups", async () => {
    await gql(SET_MANIFEST_GROUPS_ENABLED, { ids: [2], enabled: false });
    expect(pick(await gql(MANIFEST_GROUP_DETAIL, { id: 2 }), "operations.manifestGroups.group.isEnabled")).toBe(false);
    const all = await gql(SET_ALL_MANIFEST_GROUPS_ENABLED, { enabled: true });
    expect(pick(all, "operations.manifestGroups.setAllManifestGroupsEnabled.success")).toBe(true);
    const groups = items(await gql(MANIFEST_GROUPS, { take: 25 }), "operations.manifestGroups.groups.items") as unknown as {
      isEnabled: boolean;
    }[];
    expect(groups.every((g) => g.isEnabled)).toBe(true);
  });

  test("toggle an effect -> read back, then restored", async () => {
    const effects = pick(await gql(EFFECTS, {}), "operations.effects") as {
      fullName: string;
      enabled: boolean;
      toggleable: boolean;
    }[];
    const target = effects.find((e) => e.toggleable);
    if (!target) return; // a host whose effects are all infrastructure has nothing to toggle
    const flip = async (enabled: boolean) => {
      const ack = await gql(SET_EFFECT_ENABLED, { fullName: target.fullName, enabled });
      expect(pick(ack, "operations.setEffectEnabled.success")).toBe(true);
      const now = (pick(await gql(EFFECTS, {}), "operations.effects") as typeof effects).find(
        (e) => e.fullName === target.fullName,
      )!;
      expect(now.enabled).toBe(enabled);
    };
    await flip(!target.enabled);
    await flip(target.enabled);
  });

  test("scheduler failure count window -> patched", async () => {
    await gql(UPDATE_SCHEDULER, { input: { failureCountWindow: "PT2H" } });
    expect(pick(await gql(SCHEDULER_CONFIG, {}), "operations.config.scheduler.failureCountWindow")).toBe("PT2H");
  });

  test("persisted operations: upload, refused shape change, bypass, deactivate (reason required), restore", async () => {
    const id = "w2e.status.v1";
    const upload = (document: string, bypassShapeDiff = false) =>
      gql(UPLOAD_PERSISTED_OPERATION, {
        input: { id, document, description: "written by the e2e", bypassShapeDiff, version: 1 },
      });
    const payload = (d: unknown, field: string) =>
      pick(d, `operations.persistedOperations.${field}`) as {
        success: boolean;
        errors: { code: string; oldFingerprint: string | null; newFingerprint: string | null; locations: unknown }[];
      };

    expect(payload(await upload("query Status { operations { health { status } } }"), "uploadPersistedOperation").success).toBe(true);
    const listed = pick(
      await gql(PERSISTED_OPERATIONS, { filter: { idStartsWith: "w2e." }, skip: 0, take: 10 }),
      "operations.persistedOperations.persistedOperations.items",
    ) as { id: string; description: string }[];
    expect(listed).toEqual([expect.objectContaining({ id, description: "written by the e2e" })]);

    const refused = payload(
      await upload("query Status { operations { health { status queueDepth } } }"),
      "uploadPersistedOperation",
    );
    expect(refused.success).toBe(false);
    expect(refused.errors[0].code).toBe("SHAPE_DIFF_VIOLATION");
    expect(refused.errors[0].oldFingerprint).not.toBe(refused.errors[0].newFingerprint);
    expect(
      payload(await upload("query Status { operations { health { status queueDepth } } }", true), "uploadPersistedOperation")
        .success,
    ).toBe(true);

    const unparsable = payload(await upload("query Status {"), "uploadPersistedOperation");
    expect(unparsable.errors[0].code).toBe("PARSE_FAILED");
    expect(unparsable.errors[0].locations).toBeTruthy();

    const blank = payload(
      await gql(DEACTIVATE_PERSISTED_OPERATION, { input: { id, reason: "  " } }),
      "deactivatePersistedOperation",
    );
    expect(blank.success).toBe(false);
    expect(blank.errors[0].code).toBe("INVALID_INPUT");
    expect(
      payload(await gql(DEACTIVATE_PERSISTED_OPERATION, { input: { id, reason: "e2e retire" } }), "deactivatePersistedOperation")
        .success,
    ).toBe(true);
    let d = await gql(PERSISTED_OPERATION_DETAIL, { id, tenantKey: null, historyTake: 100 });
    expect(pick(d, "operations.persistedOperations.persistedOperation")).toMatchObject({
      isActive: false,
      deprecationReason: "e2e retire",
    });
    expect(
      payload(await gql(RESTORE_PERSISTED_OPERATION, { input: { id } }), "restorePersistedOperation").success,
    ).toBe(true);
    d = await gql(PERSISTED_OPERATION_DETAIL, { id, tenantKey: null, historyTake: 100 });
    expect(pick(d, "operations.persistedOperations.persistedOperation.isActive")).toBe(true);
    const history = pick(d, "operations.persistedOperations.persistedOperationHistory") as { changeType: string }[];
    expect(history.length).toBeGreaterThanOrEqual(4); // upload, bypassed upload, deactivate, restore
  });

  test("trigger manifests in one call -> queued, noted, then already queued", async () => {
    type Batch = { success: boolean; matched: number; queued: number; alreadyQueued: number; skipped: number; message: string; notes: { id: number; message: string }[] };
    const first = pick(await gql(TRIGGER_MANIFESTS, { ids: [5, 99_999] }), "operations.triggerManifests") as Batch;
    expect(first).toMatchObject({ success: true, matched: 1, queued: 1, alreadyQueued: 0, skipped: 1 });
    expect(first.notes).toEqual([{ id: 99_999, message: "Manifest 99999 not found." }]);
    const queued = items(
      await gql(WORK_QUEUE, { take: 25, manifestId: 5, status: "QUEUED" }),
      "operations.workQueue.workQueues.items",
    );
    expect(queued).toHaveLength(1);
    const again = pick(await gql(TRIGGER_MANIFESTS, { ids: [5], askAfresh: true }), "operations.triggerManifests") as Batch;
    expect(again).toMatchObject({ success: true, queued: 0, alreadyQueued: 1 });
    expect(again.message).toMatch(/asking afresh/);
    const refused = pick(await gql(TRIGGER_MANIFESTS, { ids: [] }), "operations.triggerManifests") as Batch;
    expect(refused).toMatchObject({ success: false, queued: 0, message: "No ids were given." });
  });

  test("trigger groups in one call -> each member queued, a missing group noted", async () => {
    const first = pick(await gql(TRIGGER_GROUPS, { ids: [3, 999] }), "operations.triggerGroups") as Record<string, unknown>;
    expect(first).toMatchObject({ success: true, matched: 1, queued: 1, skipped: 1 });
    expect(first.notes).toEqual([{ id: 999, message: "Manifest group 999 not found." }]);
    expect(
      items(await gql(WORK_QUEUE, { take: 25, manifestId: 6, status: "QUEUED" }), "operations.workQueue.workQueues.items"),
    ).toHaveLength(1);
    expect(pick(await gql(TRIGGER_GROUPS, { ids: [3] }), "operations.triggerGroups")).toMatchObject({ queued: 0, alreadyQueued: 1 });
    expect(pick(await gql(TRIGGER_GROUPS, { ids: [] }), "operations.triggerGroups.success")).toBe(false);
  });

  test("cancel groups' running executions in one call -> flagged", async () => {
    const ack = pick(await gql(CANCEL_GROUPS, { ids: [1, 999] }), "operations.cancelGroups") as {
      success: boolean;
      count: number;
      message: string;
    };
    expect(ack.success).toBe(true);
    expect(typeof ack.count).toBe("number");
    expect(ack.message).toMatch(/across 1 of 2 manifest group\(s\)/);
    expect(pick(await gql(EXECUTION_DETAIL, { id: 4 }), "operations.executionDetail.cancellationRequested")).toBe(true);
    expect(pick(await gql(CANCEL_GROUPS, { ids: [] }), "operations.cancelGroups.success")).toBe(false);
  });

  test("configure an effect -> written and read back, refused whole, then restored", async () => {
    type Effect = { fullName: string; isConfigurable: boolean; fields: { name: string; value: string | null }[] };
    const read = async () => (pick(await gql(EFFECTS, {}), "operations.effects") as Effect[]).find((e) => e.isConfigurable)!;
    const effect = await read();
    const valueOf = (e: Effect, name: string) => e.fields.find((f) => f.name === name)!.value;
    const original = valueOf(effect, "MaxParameterBytes");
    const ack = pick(
      await gql(CONFIGURE_EFFECT, { fullName: effect.fullName, values: [{ name: "MaxParameterBytes", value: "2048" }] }),
      "operations.configureEffect",
    );
    expect(ack).toMatchObject({ success: true, count: 1, errors: [] });
    expect(valueOf(await read(), "MaxParameterBytes")).toBe("2048");
    const refused = pick(
      await gql(CONFIGURE_EFFECT, {
        fullName: effect.fullName,
        values: [
          { name: "MaxParameterBytes", value: "4096" },
          { name: "SaveInputs", value: "maybe" },
          { name: "ShouldSaveInputs", value: "x" },
        ],
      }),
      "operations.configureEffect",
    ) as { success: boolean; errors: { field: string }[] };
    expect(refused.success).toBe(false);
    expect(refused.errors.map((e) => e.field)).toEqual(["SaveInputs", "ShouldSaveInputs"]);
    expect(valueOf(await read(), "MaxParameterBytes")).toBe("2048"); // all or nothing
    await gql(CONFIGURE_EFFECT, { fullName: effect.fullName, values: [{ name: "MaxParameterBytes", value: original }] });
    expect(valueOf(await read(), "MaxParameterBytes")).toBe(original);
  });

  test("set log levels -> in force and overridden, an unknown category refused, then restored", async () => {
    type Level = { category: string; level: string; overridden: boolean; configuredLevel: string | null };
    const trax = async () =>
      (pick(await gql(LOG_LEVELS, {}), "operations.config.logLevels") as Level[]).find((l) => l.category === "Trax")!;
    const set = async (category: string, level: string) =>
      pick(await gql(SET_LOG_LEVELS, { levels: [{ category, level }] }), "operations.config.setLogLevels") as {
        success: boolean;
        count: number;
        notApplied: string[];
        message: string;
      };
    expect(await set("Trax", "DEBUG")).toMatchObject({ success: true, count: 1, notApplied: [] });
    expect(await trax()).toMatchObject({ level: "Debug", configuredLevel: "Warning", overridden: true });
    expect(await set("No.Such.Category", "DEBUG")).toMatchObject({ success: false, count: 0 });
    expect(await set("trax", "WARNING")).toMatchObject({ success: true });
    expect((await trax()).level).toBe("Warning");
  });

  // These resolve the whole awaiting pool, so they run last (the file runs sequentially).
  test("requeue ALL dead letters -> none awaiting remain", async () => {
    const ack = await gql(REQUEUE_ALL_DEAD_LETTERS, {});
    const id = pick(ack, "operations.deadLetters.requeueAllDeadLetters.id");
    let status = pick(ack, "operations.deadLetters.requeueAllDeadLetters.status");
    for (let i = 0; status === "RUNNING" && i < 50; i++) {
      await new Promise((r) => setTimeout(r, 200));
      const job = await gql(REQUEUE_ALL_JOB, { id });
      status = pick(job, "operations.deadLetters.requeueAllJob.status");
    }
    expect(status).toBe("SUCCEEDED");
    expect(await awaitingIds()).toHaveLength(0);
  });

  test("acknowledge ALL dead letters -> none awaiting remain", async () => {
    const ack = await gql(ACKNOWLEDGE_ALL_DEAD_LETTERS, { note: "e2e ack all" });
    expect(typeof pick(ack, "operations.deadLetters.acknowledgeAllDeadLetters.count")).toBe(
      "number",
    );
    expect(await awaitingIds()).toHaveLength(0);
  });
});
