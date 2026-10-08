import { describe, expect, test } from "vitest";
import { createMockClient } from "./client";
import { createMockStore } from "./store/mock-store";
import {
  effectsScenario,
  groupScenario,
  manifestScenario,
  persistedOperationsScenario,
  schedulerConfigScenario,
} from "./scenarios";
import type { MockSchemaOverrides } from "./build-mock-schema";
import {
  EFFECTS,
  EXECUTIONS,
  LOG_LEVELS,
  EXECUTION_DETAIL,
  MANIFESTS,
  MANIFEST_DETAIL,
  MANIFEST_GROUPS,
  PERSISTED_OPERATIONS,
  PERSISTED_OPERATION_DETAIL,
  WORK_QUEUE,
  WORK_QUEUE_DETAIL,
} from "../graphql/queries";
import {
  CANCEL_EXECUTIONS,
  CANCEL_GROUPS,
  CANCEL_WORK_QUEUE_ENTRY,
  RESUME_EXECUTION,
  CONFIGURE_EFFECT,
  SET_LOG_LEVELS,
  TRIGGER_GROUPS,
  TRIGGER_MANIFESTS,
  DEACTIVATE_PERSISTED_OPERATION,
  QUEUE_TRAIN,
  REQUEUE_EXECUTION,
  RESTORE_PERSISTED_OPERATION,
  RUN_TRAIN,
  SET_ALL_MANIFEST_GROUPS_ENABLED,
  SET_EFFECT_ENABLED,
  SET_MANIFESTS_ENABLED,
  SET_MANIFESTS_REPLAY_DECISIONS_ON_RETRY,
  SET_MANIFEST_GROUPS_ENABLED,
  UPLOAD_PERSISTED_OPERATION,
} from "../graphql/mutations";

// Read-after-write for the overlays added with the Blazor-parity pages: each mutation writes the
// session delta and the matching read shows it.
function client(overrides?: MockSchemaOverrides) {
  return createMockClient({
    store: createMockStore({ exposeOnWindow: false, persist: false }),
    overrides,
    fixtures: false,
  });
}
const NET = { requestPolicy: "network-only" as const };
const get = (o: unknown, path: string) =>
  path.split(".").reduce<unknown>((a, k) => (a as Record<string, unknown>)?.[k], o);
type Row = Record<string, unknown> & { id: number };

describe("work queue / runs", () => {
  test("queueTrain returns the entry's id, and the list and detail show it", async () => {
    const c = client();
    const ack = await c
      .mutation(QUEUE_TRAIN, { input: { trainName: "Trax.Demo.OrderTrain", inputJson: '{"a":1}', priority: 3 } })
      .toPromise();
    const id = get(ack.data, "operations.workQueue.queueTrain.id") as number;
    expect(get(ack.data, "operations.workQueue.queueTrain.success")).toBe(true);
    const detail = await c.query(WORK_QUEUE_DETAIL, { id }, NET).toPromise();
    expect(get(detail.data, "operations.workQueue.detail.trainName")).toBe("Trax.Demo.OrderTrain");
    expect(get(detail.data, "operations.workQueue.detail.priority")).toBe(3);
    const list = await c.query(WORK_QUEUE, { take: 25 }, NET).toPromise();
    expect((get(list.data, "operations.workQueue.workQueues.items") as Row[])[0].id).toBe(id);
  });

  test("queueTrain and runTrain refuse input that is not JSON", async () => {
    const c = client();
    const q = await c.mutation(QUEUE_TRAIN, { input: { trainName: "T", inputJson: "{bad" } }).toPromise();
    expect(get(q.data, "operations.workQueue.queueTrain.success")).toBe(false);
    const r = await c.mutation(RUN_TRAIN, { input: { trainName: "T", inputJson: "{bad" } }).toPromise();
    expect(get(r.data, "operations.workQueue.runTrain.message")).toMatch(/not valid JSON/);
  });

  test("runTrain returns the run's id, and the execution detail shows that train", async () => {
    const c = client();
    const ack = await c.mutation(RUN_TRAIN, { input: { trainName: "Trax.Demo.PingTrain" } }).toPromise();
    const id = get(ack.data, "operations.workQueue.runTrain.id") as number;
    const detail = await c.query(EXECUTION_DETAIL, { id }, NET).toPromise();
    expect(get(detail.data, "operations.executionDetail.name")).toBe("Trax.Demo.PingTrain");
  });

  test("requeueExecution returns a work queue entry the detail page resolves", async () => {
    const c = client();
    const ack = await c.mutation(REQUEUE_EXECUTION, { id: 5, askAfresh: true }).toPromise();
    const id = get(ack.data, "operations.requeueExecution.id") as number;
    expect(get(ack.data, "operations.requeueExecution.message")).toMatch(/asks afresh/);
    const detail = await c.query(WORK_QUEUE_DETAIL, { id }, NET).toPromise();
    expect(get(detail.data, "operations.workQueue.detail.status")).toBe("QUEUED");
  });

  test("resumeExecution queues an entry the detail page resolves, and refuses a second while it is queued", async () => {
    const c = client();
    const ack = await c.mutation(RESUME_EXECUTION, { id: 902, from: "Summarize#2" }).toPromise();
    expect(get(ack.data, "operations.resumeExecution")).toMatchObject({ success: true, message: null });
    const id = get(ack.data, "operations.resumeExecution.id") as number;
    const detail = await c.query(WORK_QUEUE_DETAIL, { id }, NET).toPromise();
    expect(get(detail.data, "operations.workQueue.detail.status")).toBe("QUEUED");
    const again = await c.mutation(RESUME_EXECUTION, { id: 902, from: null }).toPromise();
    expect(get(again.data, "operations.resumeExecution")).toEqual({
      success: false,
      message: `A resume of execution 902 is already queued (WorkQueue ${id}); a run is resumed once at a time. Nothing was queued.`,
      id: null,
    });
    // Once that entry is cancelled, the run can be resumed again.
    await c.mutation(CANCEL_WORK_QUEUE_ENTRY, { id }).toPromise();
    const third = await c.mutation(RESUME_EXECUTION, { id: 902, from: null }).toPromise();
    expect(get(third.data, "operations.resumeExecution.success")).toBe(true);
  });

  test("cancelExecutions flags every selected execution", async () => {
    const c = client();
    const ack = await c.mutation(CANCEL_EXECUTIONS, { ids: [7, 8] }).toPromise();
    expect(get(ack.data, "operations.cancelExecutions.count")).toBe(2);
    for (const id of [7, 8]) {
      const d = await c.query(EXECUTION_DETAIL, { id }, NET).toPromise();
      expect(get(d.data, "operations.executionDetail.cancellationRequested")).toBe(true);
    }
  });
});

describe("manifests and groups", () => {
  test("setManifestsEnabled and setManifestsReplayDecisionsOnRetry patch list and detail", async () => {
    const c = client(manifestScenario);
    await c.mutation(SET_MANIFESTS_ENABLED, { ids: [803, 804], enabled: true }).toPromise();
    await c.mutation(SET_MANIFESTS_REPLAY_DECISIONS_ON_RETRY, { ids: [801], replay: false }).toPromise();
    const list = await c.query(MANIFESTS, { take: 25 }, NET).toPromise();
    const rows = get(list.data, "operations.manifests.items") as Row[];
    expect(rows.every((r) => r.isEnabled)).toBe(true);
    expect(rows.find((r) => r.id === 801)!.replayDecisionsOnRetry).toBe(false);
    const detail = await c.query(MANIFEST_DETAIL, { id: 801 }, NET).toPromise();
    expect(get(detail.data, "operations.manifestDetail.replayDecisionsOnRetry")).toBe(false);
  });

  test("setManifestGroupsEnabled, then setAllManifestGroupsEnabled overrides it", async () => {
    const c = client(groupScenario);
    await c.mutation(SET_MANIFEST_GROUPS_ENABLED, { ids: [2], enabled: true }).toPromise();
    let rows = get((await c.query(MANIFEST_GROUPS, { take: 25 }, NET).toPromise()).data, "operations.manifestGroups.groups.items") as Row[];
    expect(rows.find((r) => r.id === 2)!.isEnabled).toBe(true);
    await c.mutation(SET_ALL_MANIFEST_GROUPS_ENABLED, { enabled: false }).toPromise();
    rows = get((await c.query(MANIFEST_GROUPS, { take: 25 }, NET).toPromise()).data, "operations.manifestGroups.groups.items") as Row[];
    expect(rows.every((r) => r.isEnabled === false)).toBe(true);
  });
});

describe("effects", () => {
  test("setEffectEnabled flips the effect in the list", async () => {
    const c = client(effectsScenario);
    const name = "Trax.Effect.Provider.Parameter.ParameterEffectProviderFactory";
    await c.mutation(SET_EFFECT_ENABLED, { fullName: name, enabled: true }).toPromise();
    const list = await c.query(EFFECTS, {}, NET).toPromise();
    const row = (get(list.data, "operations.effects") as { fullName: string; enabled: boolean }[]).find(
      (e) => e.fullName === name,
    );
    expect(row!.enabled).toBe(true);
  });
});

describe("persisted operations", () => {
  const detail = (c: ReturnType<typeof client>, id: string, tenantKey: string | null = null) =>
    c.query(PERSISTED_OPERATION_DETAIL, { id, tenantKey, historyTake: 100 }, NET).toPromise();

  test("an upload is listed and resolvable, with an Upsert in its history", async () => {
    const c = client(persistedOperationsScenario);
    const ack = await c
      .mutation(UPLOAD_PERSISTED_OPERATION, {
        input: { id: "new.v1", document: "query New { operations { health { status } } }", description: "fresh" },
      })
      .toPromise();
    expect(get(ack.data, "operations.persistedOperations.uploadPersistedOperation.success")).toBe(true);
    const list = await c.query(PERSISTED_OPERATIONS, { skip: 0, take: 25 }, NET).toPromise();
    const page = get(list.data, "operations.persistedOperations.persistedOperations") as {
      items: { id: string }[];
      totalCount: number;
    };
    expect(page.items[0].id).toBe("new.v1");
    expect(page.totalCount).toBe(4);
    const d = await detail(c, "new.v1");
    expect(get(d.data, "operations.persistedOperations.persistedOperation.operationName")).toBe("New");
    expect(get(d.data, "operations.persistedOperations.persistedOperationHistory.0.changeType")).toBe("Upsert");
  });

  test("a shape-changing edit is refused with both fingerprints unless bypassed", async () => {
    const c = client(persistedOperationsScenario);
    const up = (document: string, bypassShapeDiff = false) =>
      c.mutation(UPLOAD_PERSISTED_OPERATION, { input: { id: "s.v1", document, bypassShapeDiff } }).toPromise();
    await up("query S { operations { health { status } } }");
    const refused = await up("query S { operations { health { status queueDepth } } }");
    const payload = get(refused.data, "operations.persistedOperations.uploadPersistedOperation") as {
      success: boolean;
      errors: { code: string; oldFingerprint: string; newFingerprint: string }[];
    };
    expect(payload.success).toBe(false);
    expect(payload.errors[0].code).toBe("SHAPE_DIFF_VIOLATION");
    expect(payload.errors[0].oldFingerprint).not.toBe(payload.errors[0].newFingerprint);
    const bypassed = await up("query S { operations { health { status queueDepth } } }", true);
    expect(get(bypassed.data, "operations.persistedOperations.uploadPersistedOperation.success")).toBe(true);
  });

  test("an unbalanced document is a parse error with its location", async () => {
    const c = client(persistedOperationsScenario);
    const r = await c
      .mutation(UPLOAD_PERSISTED_OPERATION, { input: { id: "p.v1", document: "query P {" } })
      .toPromise();
    expect(get(r.data, "operations.persistedOperations.uploadPersistedOperation.errors.0.code")).toBe("PARSE_FAILED");
    expect(get(r.data, "operations.persistedOperations.uploadPersistedOperation.errors.0.locations.0.line")).toBe(1);
  });

  test("deactivate needs a reason, then restore brings the operation back", async () => {
    const c = client(persistedOperationsScenario);
    const blank = await c
      .mutation(DEACTIVATE_PERSISTED_OPERATION, { input: { id: "greet.v1", reason: "  " } })
      .toPromise();
    expect(get(blank.data, "operations.persistedOperations.deactivatePersistedOperation.errors.0.code")).toBe(
      "INVALID_INPUT",
    );
    await c
      .mutation(DEACTIVATE_PERSISTED_OPERATION, { input: { id: "greet.v1", reason: "retired" } })
      .toPromise();
    let d = await detail(c, "greet.v1");
    expect(get(d.data, "operations.persistedOperations.persistedOperation.isActive")).toBe(false);
    expect(get(d.data, "operations.persistedOperations.persistedOperation.deprecationReason")).toBe("retired");
    const list = await c.query(PERSISTED_OPERATIONS, { skip: 0, take: 25 }, NET).toPromise();
    const row = (
      get(list.data, "operations.persistedOperations.persistedOperations.items") as { id: string; isActive: boolean }[]
    ).find((r) => r.id === "greet.v1");
    expect(row!.isActive).toBe(false);
    await c.mutation(RESTORE_PERSISTED_OPERATION, { input: { id: "greet.v1" } }).toPromise();
    d = await detail(c, "greet.v1");
    expect(get(d.data, "operations.persistedOperations.persistedOperation.isActive")).toBe(true);
    expect(get(d.data, "operations.persistedOperations.persistedOperationHistory.0.changeType")).toBe("Restore");
  });
});

describe("batch triggers", () => {
  test("triggerManifests queues an entry per manifest, and a second trigger finds it already queued", async () => {
    const c = client(manifestScenario);
    const first = get(
      (await c.mutation(TRIGGER_MANIFESTS, { ids: [801, 802, 801] }).toPromise()).data,
      "operations.triggerManifests",
    ) as Record<string, unknown>;
    expect(first).toMatchObject({ success: true, matched: 2, queued: 2, alreadyQueued: 0, skipped: 0, notes: [] });
    const entries = get(
      (await c.query(WORK_QUEUE, { take: 25, manifestId: 801 }, NET).toPromise()).data,
      "operations.workQueue.workQueues.items",
    ) as Row[];
    expect(entries.filter((e) => e.manifestId === 801 && e.status === "QUEUED")).toHaveLength(1);
    const again = get(
      (await c.mutation(TRIGGER_MANIFESTS, { ids: [801], askAfresh: true }).toPromise()).data,
      "operations.triggerManifests",
    ) as Record<string, unknown>;
    expect(again).toMatchObject({ queued: 0, alreadyQueued: 1 });
    expect(again.message).toMatch(/asking afresh\.$/);
  });

  test("a deleted manifest is skipped with a note; an empty batch is refused", async () => {
    const c = client(manifestScenario);
    const noted = get(
      (await c.mutation(TRIGGER_MANIFESTS, { ids: [803, 1_000_009] }).toPromise()).data,
      "operations.triggerManifests",
    ) as Record<string, unknown>;
    expect(noted).toMatchObject({ success: true, matched: 1, queued: 1, skipped: 1 });
    expect(noted.notes).toEqual([{ id: 1_000_009, message: "Manifest 1000009 not found." }]);
    const refused = get(
      (await c.mutation(TRIGGER_MANIFESTS, { ids: [] }).toPromise()).data,
      "operations.triggerManifests",
    ) as Record<string, unknown>;
    expect(refused).toMatchObject({ success: false, message: "No ids were given.", queued: 0 });
    const tooMany = Array.from({ length: 1001 }, (_, i) => i + 1);
    expect(
      get((await c.mutation(TRIGGER_MANIFESTS, { ids: tooMany }).toPromise()).data, "operations.triggerManifests.success"),
    ).toBe(false);
  });

  test("triggerGroups counts each group's manifests, then finds them already queued", async () => {
    const c = client(groupScenario);
    const first = get((await c.mutation(TRIGGER_GROUPS, { ids: [1, 2] }).toPromise()).data, "operations.triggerGroups");
    expect(first).toMatchObject({ success: true, matched: 2, queued: 4, alreadyQueued: 0 });
    const again = get((await c.mutation(TRIGGER_GROUPS, { ids: [2, 1_000_002] }).toPromise()).data, "operations.triggerGroups");
    expect(again).toMatchObject({ matched: 1, queued: 0, alreadyQueued: 2, skipped: 1 });
    expect(get((await c.mutation(TRIGGER_GROUPS, { ids: [] }).toPromise()).data, "operations.triggerGroups.success")).toBe(false);
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
    const ack = get((await c.mutation(CANCEL_GROUPS, { ids: [1] }).toPromise()).data, "operations.cancelGroups");
    expect(ack).toMatchObject({ success: true, count: 1 });
    const rows = get(
      (await c.query(EXECUTIONS, { take: 25, manifestGroupId: 1 }, NET).toPromise()).data,
      "operations.executions.items",
    ) as Row[];
    expect(rows.map((r) => r.cancellationRequested)).toEqual([true, false]);
    const other = get(
      (await c.query(EXECUTIONS, { take: 25, manifestGroupId: 2 }, NET).toPromise()).data,
      "operations.executions.items",
    ) as Row[];
    expect(other[0].cancellationRequested).toBe(false);
    expect(get((await c.mutation(CANCEL_GROUPS, { ids: [] }).toPromise()).data, "operations.cancelGroups.message")).toBe(
      "No ids were given.",
    );
  });
});

describe("effect settings", () => {
  const JSON_EFFECT = "Trax.Effect.Provider.Json.JsonEffectProviderFactory";
  type Field = { name: string; value: string | null; hasValue: boolean };
  const fieldsOf = async (c: ReturnType<typeof client>) =>
    (
      (get((await c.query(EFFECTS, {}, NET).toPromise()).data, "operations.effects") as {
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
          .mutation(CONFIGURE_EFFECT, {
            fullName: JSON_EFFECT,
            values: [
              { name: "MaxBytes", value: "2048" },
              { name: "LogLevel", value: "Debug" },
              { name: "ConnectionString", value: "s3cret" },
            ],
          })
          .toPromise()
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
    await c.mutation(CONFIGURE_EFFECT, { fullName: JSON_EFFECT, values: [{ name: "MaxBytes", value: "" }] }).toPromise();
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
          .mutation(CONFIGURE_EFFECT, {
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
          .toPromise()
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
      (await c.mutation(CONFIGURE_EFFECT, { fullName: "X.Y", values: [{ name: "A", value: "1" }] }).toPromise()).data,
      "operations.configureEffect.message",
    );
    expect(unknown).toBe("No effect named 'X.Y' is registered in this process.");
    const none = get(
      (await c.mutation(CONFIGURE_EFFECT, { fullName: JSON_EFFECT, values: [] }).toPromise()).data,
      "operations.configureEffect.message",
    );
    expect(none).toBe("No settings were given.");
  });
});

describe("log levels", () => {
  type Level = { category: string; level: string; overridden: boolean; configuredLevel: string | null };
  const levels = async (c: ReturnType<typeof client>) =>
    get((await c.query(LOG_LEVELS, {}, NET).toPromise()).data, "operations.config.logLevels") as Level[];

  test("setLogLevels sets a configured category, matched ignoring case, and the read shows it overridden", async () => {
    const c = client(schedulerConfigScenario);
    await levels(c);
    const ack = get(
      (await c.mutation(SET_LOG_LEVELS, { levels: [{ category: "microsoft.aspnetcore", level: "ERROR" }] }).toPromise()).data,
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
      (await c.mutation(SET_LOG_LEVELS, { levels: [{ category: "Microsoft.Hosting.Lifetime", level: "WARNING" }] }).toPromise())
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
          .mutation(SET_LOG_LEVELS, {
            levels: [
              { category: "Default", level: "DEBUG" },
              { category: "Nope", level: "DEBUG" },
            ],
          })
          .toPromise()
      ).data,
      "operations.config.setLogLevels",
    );
    expect(refused).toMatchObject({ success: false, message: "Not a category configured under Logging:LogLevel: Nope." });
    expect((await levels(c)).find((l) => l.category === "Default")!.level).toBe("Information");
    expect(
      get((await c.mutation(SET_LOG_LEVELS, { levels: [] }).toPromise()).data, "operations.config.setLogLevels.message"),
    ).toBe("No levels were given.");
  });
});
