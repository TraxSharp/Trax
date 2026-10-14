import { beforeAll, describe, expect, test } from "vitest";
import { E2E_ORIGIN, E2E_URL, gql, items, nextEvent, pick, reachable } from "./helpers";
import { ON_JUNCTION_EVENT } from "../graphql/subscriptions";
import {
  DEAD_LETTERS,
  DEAD_LETTER_DETAIL,
  DECISIONS,
  SERVER_VERSION,
  EFFECTS,
  ENVIRONMENT_NAME,
  EXECUTIONS,
  JUNCTION_RUNS,
  LOG_LEVELS,
  EXECUTION_DETAIL,
  LOGS,
  MANIFESTS,
  MANIFEST_DETAIL,
  MANIFEST_EXCLUSIONS,
  MANIFEST_GROUPS,
  MANIFEST_GROUP_DETAIL,
  MANIFEST_GROUP_STATS,
  MANIFEST_STATS,
  OVERVIEW,
  PERSISTED_OPERATIONS,
  PERSISTED_OPERATIONS_AVAILABLE,
  PERSISTED_OPERATION_DETAIL,
  SCHEDULER_CONFIG,
  TRAINS,
  WORK_QUEUE,
  WORK_QUEUE_DETAIL,
} from "../graphql/queries";

beforeAll(async () => {
  if (!(await reachable()))
    throw new Error(`e2e devhost unreachable at ${E2E_URL}; run \`npm run test:e2e\``);
});

// Contract checks: every read the dashboard issues returns against the real API with the shape
// the UI expects. Asserts shape/fields (robust to concurrent writes), not specific values.
describe("reads (contract)", () => {
  test("overview: health + dashboard metrics", async () => {
    const d = await gql(OVERVIEW, { range: "LAST24_HOURS", hideAdmin: true });
    expect(pick(d, "operations.health.status")).toBeTypeOf("string");
    expect(pick(d, "operations.metrics.dashboard.kpis")).toBeTruthy();
    expect(Array.isArray(pick(d, "operations.metrics.dashboard.executionsOverTime"))).toBe(true);
  });

  test("trains registry", async () => {
    const d = await gql(TRAINS, { hideAdmin: true });
    expect(Array.isArray(pick(d, "operations.trains"))).toBe(true);
  });

  test("work queue list has seeded rows with a status", async () => {
    const d = await gql(WORK_QUEUE, { take: 5 });
    const rows = items(d, "operations.workQueue.workQueues.items");
    expect(rows.length).toBeGreaterThan(0);
    expect(rows[0]).toHaveProperty("status");
  });

  test("dead letters list", async () => {
    const d = await gql(DEAD_LETTERS, { take: 5 });
    expect(items(d, "operations.deadLetters.deadLetters.items").length).toBeGreaterThan(0);
  });

  test("manifests + manifest groups + logs lists", async () => {
    expect(items(await gql(MANIFESTS, { take: 5 }), "operations.manifests.items").length).toBeGreaterThan(0);
    expect(
      items(await gql(MANIFEST_GROUPS, { take: 5 }), "operations.manifestGroups.groups.items").length,
    ).toBeGreaterThan(0);
    expect(Array.isArray(pick(await gql(LOGS, { take: 5 }), "operations.logs.logs.items"))).toBe(true);
  });

  test("executions list", async () => {
    const d = await gql(EXECUTIONS, { take: 5 });
    expect(items(d, "operations.executions.items").length).toBeGreaterThan(0);
  });

  test("scheduler config", async () => {
    const d = await gql(SCHEDULER_CONFIG, {});
    expect(pick(d, "operations.config.scheduler.maxActiveJobs")).toBeDefined();
  });

  test("detail queries resolve the seeded rows", async () => {
    const manifestId = items(await gql(MANIFESTS, { take: 1 }), "operations.manifests.items")[0].id;
    expect(pick(await gql(MANIFEST_DETAIL, { id: manifestId }), "operations.manifestDetail.id")).toBe(manifestId);

    const groupId = items(await gql(MANIFEST_GROUPS, { take: 1 }), "operations.manifestGroups.groups.items")[0].id;
    expect(pick(await gql(MANIFEST_GROUP_DETAIL, { id: groupId }), "operations.manifestGroups.group.id")).toBe(groupId);

    const wqId = items(await gql(WORK_QUEUE, { take: 1 }), "operations.workQueue.workQueues.items")[0].id;
    expect(pick(await gql(WORK_QUEUE_DETAIL, { id: wqId }), "operations.workQueue.detail.id")).toBe(wqId);

    const dlId = items(await gql(DEAD_LETTERS, { take: 1 }), "operations.deadLetters.deadLetters.items")[0].id;
    expect(pick(await gql(DEAD_LETTER_DETAIL, { id: dlId }), "operations.deadLetters.deadLetter.id")).toBe(dlId);

    const execId = items(await gql(EXECUTIONS, { take: 1 }), "operations.executions.items")[0].id;
    expect(pick(await gql(EXECUTION_DETAIL, { id: execId }), "operations.executionDetail.id")).toBe(execId);
  });
});

// The new manifest/group-scoped surface + effects + CPU, exercised against the real server.
describe("reads (new manifest/group/effects surface)", () => {
  test("manifestStats returns per-state counts for a seeded manifest", async () => {
    const manifestId = items(await gql(MANIFESTS, { take: 1 }), "operations.manifests.items")[0].id;
    const s = pick(await gql(MANIFEST_STATS, { manifestId }), "operations.manifestStats") as {
      manifestId: number;
      total: number;
    };
    expect(s.manifestId).toBe(manifestId);
    expect(typeof s.total).toBe("number");
  });

  test("manifestGroups.stats returns a row per requested group id", async () => {
    const groupId = items(
      await gql(MANIFEST_GROUPS, { take: 1 }),
      "operations.manifestGroups.groups.items",
    )[0].id;
    const stats = pick(
      await gql(MANIFEST_GROUP_STATS, { groupIds: [groupId] }),
      "operations.manifestGroups.stats",
    ) as Array<{ groupId: number; manifestCount: number }>;
    expect(stats).toHaveLength(1);
    expect(stats[0].groupId).toBe(groupId);
    expect(typeof stats[0].manifestCount).toBe("number");
  });

  test("manifestExclusions returns an array", async () => {
    const manifestId = items(await gql(MANIFESTS, { take: 1 }), "operations.manifests.items")[0].id;
    expect(
      Array.isArray(
        pick(await gql(MANIFEST_EXCLUSIONS, { manifestId }), "operations.manifestExclusions"),
      ),
    ).toBe(true);
  });

  test("effects list exposes enabled + toggleable", async () => {
    const effects = pick(await gql(EFFECTS, {}), "operations.effects") as Array<{
      enabled: boolean;
      toggleable: boolean;
    }>;
    expect(Array.isArray(effects)).toBe(true);
    if (effects.length) {
      expect(typeof effects[0].enabled).toBe("boolean");
      expect(typeof effects[0].toggleable).toBe("boolean");
    }
  });

  test("executions filtered by manifestId only return that manifest's runs", async () => {
    const rows = items(await gql(EXECUTIONS, { take: 25 }), "operations.executions.items") as Array<{
      id: number;
      manifestId: number | null;
    }>;
    const withManifest = rows.find((e) => e.manifestId != null);
    if (!withManifest) return; // seed may not link executions to manifests
    const scoped = items(
      await gql(EXECUTIONS, { take: 25, manifestId: withManifest.manifestId }),
      "operations.executions.items",
    ) as unknown as Array<{ manifestId: number | null }>;
    expect(scoped.every((r) => r.manifestId === withManifest.manifestId)).toBe(true);
  });

  test("manifests filtered by manifestGroupId only return that group's manifests", async () => {
    const first = items(await gql(MANIFESTS, { take: 1 }), "operations.manifests.items")[0] as {
      id: number;
      manifestGroupId: number;
    };
    const scoped = items(
      await gql(MANIFESTS, { take: 25, manifestGroupId: first.manifestGroupId }),
      "operations.manifests.items",
    ) as unknown as Array<{ manifestGroupId: number }>;
    expect(scoped.length).toBeGreaterThan(0);
    expect(scoped.every((r) => r.manifestGroupId === first.manifestGroupId)).toBe(true);
  });

  test("overview exposes the serverCpuPercent field", async () => {
    const metrics = pick(
      await gql(OVERVIEW, { range: "LAST24_HOURS", hideAdmin: true }),
      "operations.metrics",
    ) as Record<string, unknown>;
    expect("serverCpuPercent" in metrics).toBe(true);
  });
});

// The surface the Blazor-parity pages added, against the seeded rows (scripts/e2e-seed.sql).
describe("reads (parity surface)", () => {
  test("executionDetail carries the run-detail fields", async () => {
    const run = pick(await gql(EXECUTION_DETAIL, { id: 6 }), "operations.executionDetail") as Record<string, unknown>;
    expect(run.parentId).toBe(1);
    expect(run.replayDecisionsOf).toBe(2);
    expect(run.executor).toBe("JobRunner");
    expect(run.scheduledTime).toBeTypeOf("string");
    expect(run.hostInstanceId).toBe("e2e-instance");
    expect(JSON.parse(run.hostLabels as string)).toEqual({ region: "eu-west" });
    const failed = pick(await gql(EXECUTION_DETAIL, { id: 2 }), "operations.executionDetail") as Record<string, unknown>;
    expect(failed.failureClass).toBe("TRANSIENT");
    expect(failed.stackTrace).toBe("at Trax.E2E.ChargeCard()");
  });

  test("executions(failureClass) returns only runs that failed that way, with the column", async () => {
    const rows = items(
      await gql(EXECUTIONS, { take: 25, failureClass: "TRANSIENT" }),
      "operations.executions.items",
    ) as unknown as { id: number; failureClass: string }[];
    expect(rows.map((r) => r.id)).toEqual([2]);
    expect(rows[0].failureClass).toBe("TRANSIENT");
    const failedOnManifest = items(
      await gql(EXECUTIONS, { take: 1, manifestId: 2, trainState: "FAILED" }),
      "operations.executions.items",
    );
    expect(failedOnManifest.map((r) => r.id)).toEqual([2]);
  });

  test("junctionRuns reads a run's steps in order, and pages from afterPosition", async () => {
    const steps = pick(
      await gql(JUNCTION_RUNS, { metadataId: 2, take: 500 }),
      "operations.junctionRuns",
    ) as Record<string, unknown>[];
    expect(steps.map((s) => s.position)).toEqual([0, 1, 2]);
    expect(steps[1]).toMatchObject({ kind: "YES_NO", answer: "no", confidence: 0.92, replayed: true, attempt: 2 });
    expect(steps[2]).toMatchObject({ state: "FAILED", failureClass: "TRANSIENT", failureException: "TimeoutException" });
    expect(steps[0].durationMs).toBe(2000);
    const after = pick(
      await gql(JUNCTION_RUNS, { metadataId: 2, afterPosition: 0, take: 1 }),
      "operations.junctionRuns",
    ) as Record<string, unknown>[];
    expect(after.map((s) => s.position)).toEqual([1]);
    expect(pick(await gql(JUNCTION_RUNS, { metadataId: 1, take: 500 }), "operations.junctionRuns")).toEqual([]);
  });

  test("workQueue list and detail carry subject, staging, input and what an entry waits on", async () => {
    const rows = items(
      await gql(WORK_QUEUE, { take: 25 }),
      "operations.workQueue.workQueues.items",
    ) as unknown as { id: number; subjectKey: string | null; confirmedAt: string | null }[];
    const staged = rows.find((r) => r.id === 6)!;
    expect(staged.subjectKey).toBe("order-42");
    expect(staged.confirmedAt).toBeNull();
    const detail = pick(await gql(WORK_QUEUE_DETAIL, { id: 6 }), "operations.workQueue.detail") as Record<string, unknown>;
    expect(detail.subjectHeldBy).toBe(7);
    expect(detail.subjectQueuedBehind).toBeNull();
    expect(JSON.parse(detail.input as string)).toEqual({ label: "subject" });
    expect(pick(await gql(WORK_QUEUE_DETAIL, { id: 999_999 }), "operations.workQueue.detail")).toBeNull();
  });

  test("manifestDetail carries the group name, schedule details and properties", async () => {
    const m = pick(await gql(MANIFEST_DETAIL, { id: 1 }), "operations.manifestDetail") as Record<string, unknown>;
    expect(m.manifestGroupName).toBe("e2e-group-alpha");
    expect(m.varianceSeconds).toBe(30);
    expect(m.misfirePolicy).toBeTypeOf("string");
    expect(m.replayDecisionsOnRetry).toBeTypeOf("boolean");
    expect(m.propertyTypeName).toBe("Trax.Dashboard.DevHost.PingInput");
    expect(JSON.parse(m.properties as string)).toEqual({ label: "e2e-seed" });
    const list = items(await gql(MANIFESTS, { take: 25 }), "operations.manifests.items") as unknown as {
      replayDecisionsOnRetry: boolean;
    }[];
    expect(list.every((r) => typeof r.replayDecisionsOnRetry === "boolean")).toBe(true);
  });

  test("config: environment name, log levels and the failure count window", async () => {
    expect(pick(await gql(ENVIRONMENT_NAME, {}), "operations.config.environmentName")).toBe("Development");
    expect(Array.isArray(pick(await gql(LOG_LEVELS, {}), "operations.config.logLevels"))).toBe(true);
    expect(pick(await gql(SCHEDULER_CONFIG, {}), "operations.config.scheduler.failureCountWindow")).toBeTypeOf(
      "string",
    );
  });

  test("effects carry their configuration fields; trains their enum values", async () => {
    const effects = pick(await gql(EFFECTS, {}), "operations.effects") as Record<string, unknown>[];
    for (const e of effects) {
      expect(typeof e.isConfigurable).toBe("boolean");
      expect("configuration" in e && "configurationTypeName" in e).toBe(true);
    }
    const trains = pick(await gql(TRAINS, { hideAdmin: true }), "operations.trains") as {
      inputSchema: { enumValues: unknown }[];
    }[];
    for (const t of trains) for (const p of t.inputSchema) expect("enumValues" in p).toBe(true);
  });

  test("persisted operations: the probe, the filtered list, and one operation with its history", async () => {
    expect(
      pick(await gql(PERSISTED_OPERATIONS_AVAILABLE, {}), "operations.persistedOperations.__typename"),
    ).toBe("PersistedOperationQueries");
    const ids = async (filter: Record<string, unknown> | null) =>
      (
        pick(
          await gql(PERSISTED_OPERATIONS, { filter, skip: 0, take: 50 }),
          "operations.persistedOperations.persistedOperations.items",
        ) as { id: string }[]
      )
        .map((r) => r.id)
        .filter((id) => id.startsWith("e2e."))
        .sort();
    expect(await ids(null)).toEqual(["e2e.greet.v0", "e2e.greet.v1", "e2e.orders"]);
    expect(await ids({ tenantKey: "" })).toEqual(["e2e.greet.v0", "e2e.greet.v1"]);
    expect(await ids({ tenantKey: "acme" })).toEqual(["e2e.orders"]);
    expect(await ids({ isActive: false })).toEqual(["e2e.greet.v0"]);
    expect(await ids({ idStartsWith: "e2e.greet" })).toEqual(["e2e.greet.v0", "e2e.greet.v1"]);

    const d = await gql(PERSISTED_OPERATION_DETAIL, { id: "e2e.orders", tenantKey: "acme", historyTake: 100 });
    expect(pick(d, "operations.persistedOperations.persistedOperation")).toMatchObject({
      id: "e2e.orders",
      tenantKey: "acme",
      operationName: "Orders",
      version: 3,
      isActive: true,
    });
    const missing = await gql(PERSISTED_OPERATION_DETAIL, { id: "e2e.orders", tenantKey: null, historyTake: 100 });
    expect(pick(missing, "operations.persistedOperations.persistedOperation")).toBeNull();
    const history = pick(
      await gql(PERSISTED_OPERATION_DETAIL, { id: "e2e.greet.v0", tenantKey: null, historyTake: 100 }),
      "operations.persistedOperations.persistedOperationHistory",
    ) as { changeType: string; changedReason: string | null }[];
    expect(history[0]).toMatchObject({ changeType: "Deactivate", changedReason: "superseded" });
  });
});

// The surface added for the second parity round, against the seeded rows.
describe("reads (filters, decisions, logs, config)", () => {
  const ids = (d: unknown, path: string) => items(d, path).map((r) => r.id);

  test("trains say which override QueueSubjectKey", async () => {
    const trains = pick(await gql(TRAINS, { hideAdmin: true }), "operations.trains") as {
      fullName: string;
      hasQueueSubjectKey: boolean;
    }[];
    const byName = Object.fromEntries(trains.map((t) => [t.fullName, t.hasQueueSubjectKey]));
    expect(byName["Trax.Dashboard.DevHost.ISubjectPingTrain"]).toBe(true);
    expect(byName["Trax.Dashboard.DevHost.IBroadcastPingTrain"]).toBe(false);
  });

  test("manifests(hideAdminTrains) leaves out the admin train's manifest", async () => {
    expect(ids(await gql(MANIFESTS, { take: 25, hideAdminTrains: false }), "operations.manifests.items")).toContain(7);
    expect(ids(await gql(MANIFESTS, { take: 25, hideAdminTrains: true }), "operations.manifests.items")).not.toContain(7);
  });

  test("executions by external id, parent and host, with the parent and running junction columns", async () => {
    const byExternal = items(await gql(EXECUTIONS, { take: 25, externalId: "e2e-meta-6" }), "operations.executions.items") as unknown as {
      id: number;
      parentId: number | null;
    }[];
    expect(byExternal.map((r) => r.id)).toEqual([6]);
    expect(byExternal[0].parentId).toBe(1);
    expect(ids(await gql(EXECUTIONS, { take: 25, parentId: 1 }), "operations.executions.items")).toEqual([6]);
    expect(ids(await gql(EXECUTIONS, { take: 25, hostName: "e2e-host" }), "operations.executions.items")).toEqual([6]);
    expect(ids(await gql(EXECUTIONS, { take: 25, hostName: "no-such-host" }), "operations.executions.items")).toEqual([]);
    const running = items(
      await gql(EXECUTIONS, { take: 25, trainState: "IN_PROGRESS" }),
      "operations.executions.items",
    ) as unknown as { id: number; currentlyRunningJunction: string | null }[];
    expect(running.find((r) => r.id === 3)!.currentlyRunningJunction).toBe("LoadCart");
  });

  test("work queue by subject and by manifest, with what an entry replays", async () => {
    expect(ids(await gql(WORK_QUEUE, { take: 25, subjectKey: "order-42" }), "operations.workQueue.workQueues.items").sort()).toEqual([6, 7]);
    const byManifest = items(
      await gql(WORK_QUEUE, { take: 25, manifestId: 3 }),
      "operations.workQueue.workQueues.items",
    ) as unknown as { id: number; manifestId: number; replayDecisionsOf: number | null }[];
    expect(byManifest.every((r) => r.manifestId === 3)).toBe(true);
    expect(byManifest.find((r) => r.id === 5)!.replayDecisionsOf).toBe(2);
    expect(pick(await gql(WORK_QUEUE_DETAIL, { id: 5 }), "operations.workQueue.detail.replayDecisionsOf")).toBe(2);
  });

  test("dead letters by manifest", async () => {
    expect(ids(await gql(DEAD_LETTERS, { take: 25, manifestId: 2 }), "operations.deadLetters.deadLetters.items").sort()).toEqual([2, 4]);
  });

  test("a run's log oldest first, paged forward, and the text filters", async () => {
    const page1 = pick(await gql(LOGS, { take: 2, metadataId: 2, order: "OLDEST" }), "operations.logs.logs") as {
      items: { id: number; message: string }[];
      nextCursor: number;
      totalCount: number;
      isCountCapped: boolean;
    };
    expect(page1.items.map((l) => l.message)).toEqual(["Charging card for order 42", "Payment context loaded"]);
    expect(page1.totalCount).toBe(4);
    expect(page1.isCountCapped).toBe(false);
    const page2 = pick(
      await gql(LOGS, { take: 2, metadataId: 2, order: "OLDEST", afterId: page1.nextCursor }),
      "operations.logs.logs.items",
    ) as { message: string }[];
    expect(page2.map((l) => l.message)).toEqual(["Gateway slow, retrying", "Gateway timed out after 30s"]);
    const message = pick(await gql(LOGS, { take: 25, messageContains: "GATEWAY" }), "operations.logs.logs") as {
      items: { message: string }[];
      totalCount: number;
    };
    expect(message.items.map((l) => l.message)).toEqual(["Gateway timed out after 30s", "Gateway slow, retrying"]);
    expect(message.totalCount).toBe(2);
    expect(
      pick(await gql(LOGS, { take: 25, categoryContains: "payments", minimumLevel: "WARNING" }), "operations.logs.logs.totalCount"),
    ).toBe(2);
  });

  test("a text filter past 10,000 matches caps its count", async () => {
    const capped = pick(await gql(LOGS, { take: 5, messageContains: "heartbeat" }), "operations.logs.logs") as {
      items: unknown[];
      totalCount: number;
      isCountCapped: boolean;
    };
    expect(capped).toMatchObject({ totalCount: 10_000, isCountCapped: true });
    expect(capped.items).toHaveLength(5);
    expect(pick(await gql(LOGS, { take: 5 }), "operations.logs.logs.isCountCapped")).toBe(false);
  });

  test("decisions page by cursor, withholding a sensitive answer and the track taken on it", async () => {
    type D = Record<string, unknown> & { id: number };
    const first = pick(await gql(DECISIONS, { metadataId: 2, take: 2 }), "operations.decisions") as {
      items: D[];
      nextCursor: number;
    };
    expect(first.items.map((d) => d.questionKey)).toEqual(["is-fraud", "risk"]);
    expect(first.items[0]).toMatchObject({ kind: "yes_no", answer: '"no"', decider: "Trax.E2E.FraudDecider", answerWithheld: false });
    expect(first.items[1].replayRefused).toBe("the question changed since the run it replays");
    const rest = pick(
      await gql(DECISIONS, { metadataId: 2, take: 50, afterId: first.nextCursor }),
      "operations.decisions.items",
    ) as D[];
    expect(rest[0]).toMatchObject({ questionKey: "DevhostCarrier", answer: null, routes: null, answerWithheld: true, trackWithheld: false });
    expect(rest[1]).toMatchObject({ questionKey: null, answer: null, answerWithheld: true, trackWithheld: true });
    // A page that starts on the withheld track knows it from the decisions before it.
    const onTrack = pick(
      await gql(DECISIONS, { metadataId: 2, take: 50, afterId: rest[0].id }),
      "operations.decisions.items",
    ) as D[];
    expect(onTrack[0]).toMatchObject({ trackWithheld: true, kind: null });
    const refused = pick(await gql(DECISIONS, { metadataId: 6, take: 50 }), "operations.decisions.items") as D[];
    expect(refused[0]).toMatchObject({ isRefused: true, replayed: true, refused: "the answer is not one of yes or no" });
    expect(pick(await gql(DECISIONS, { metadataId: 1, take: 50 }), "operations.decisions")).toMatchObject({ items: [], nextCursor: null });
  });

  test("a run that abandoned its replay says so", async () => {
    expect(pick(await gql(EXECUTION_DETAIL, { id: 6 }), "operations.executionDetail.replayAbandoned")).toBe(true);
    expect(pick(await gql(EXECUTION_DETAIL, { id: 2 }), "operations.executionDetail.replayAbandoned")).toBe(false);
  });

  test("config: the Trax version and the configured log levels", async () => {
    expect(pick(await gql(SERVER_VERSION, {}), "operations.config.version")).toMatch(/^\d+\.\d+\.\d+/);
    const levels = pick(await gql(LOG_LEVELS, {}), "operations.config.logLevels") as Record<string, unknown>[];
    expect(levels[0]).toMatchObject({ category: "Default", configuredLevel: "Warning" });
    expect(levels.every((l) => typeof l.overridden === "boolean")).toBe(true);
  });

  test("effects describe each setting for an editor", async () => {
    const effects = pick(await gql(EFFECTS, {}), "operations.effects") as {
      isConfigurable: boolean;
      fields: { name: string; kind: string; hint: string; sensitive: boolean }[];
    }[];
    const configurable = effects.find((e) => e.isConfigurable)!;
    const byName = Object.fromEntries(configurable.fields.map((f) => [f.name, f]));
    expect(byName.SaveInputs.kind).toBe("BOOLEAN");
    expect(byName.MaxParameterBytes).toMatchObject({ kind: "TEXT", hint: "Enter a whole number" });
    expect(byName.ShouldSaveInputs.kind).toBe("SET_IN_CODE");
    for (const e of effects.filter((x) => !x.isConfigurable)) expect(e.fields).toEqual([]);
  });
});

// A real run's steps, live: the devhost's /dev/delay starts a train whose one junction waits 10s.
// The step is read while it runs, onJunctionEvent delivers its end, and the read after shows it
// ended — the path the junction timeline takes.
describe("junction events (live)", () => {
  test("a running step is read, then its end arrives over onJunctionEvent", async () => {
    const res = await fetch(`${E2E_ORIGIN}/dev/delay`, { method: "POST" });
    expect(res.ok).toBe(true);
    let runId: number | undefined;
    for (let i = 0; i < 50 && runId == null; i++) {
      const rows = items(
        await gql(EXECUTIONS, {
          take: 1,
          trainName: "Trax.Dashboard.DevHost.IBroadcastDelayTrain",
          trainState: "IN_PROGRESS",
        }),
        "operations.executions.items",
      );
      runId = rows[0]?.id;
      if (runId == null) await new Promise((r) => setTimeout(r, 100));
    }
    expect(runId).toBeTypeOf("number");

    const sub = nextEvent<{ onJunctionEvent: { eventType: string; junction: { state: string; position: number } } }>(
      ON_JUNCTION_EVENT,
      { metadataId: runId },
      (d) => d.onJunctionEvent.eventType === "JUNCTION_COMPLETED",
      20_000,
    );
    try {
      await sub.ready;
      const running = pick(
        await gql(JUNCTION_RUNS, { metadataId: runId, take: 500 }),
        "operations.junctionRuns",
      ) as { position: number; state: string; name: string }[];
      // The writer stores steps in the background; the started step is there or about to be.
      if (running.length > 0) expect(running[0]).toMatchObject({ position: 0, name: "DelayJunction" });

      const done = await sub.event;
      expect(done.onJunctionEvent.junction).toMatchObject({ position: 0, state: "COMPLETED" });
    } finally {
      sub.dispose();
    }

    let after: { state: string; durationMs: number | null }[] = [];
    for (let i = 0; i < 30; i++) {
      after = pick(
        await gql(JUNCTION_RUNS, { metadataId: runId, afterPosition: -1, take: 500 }),
        "operations.junctionRuns",
      ) as typeof after;
      if (after[0]?.state === "COMPLETED") break;
      await new Promise((r) => setTimeout(r, 100));
    }
    expect(after[0].state).toBe("COMPLETED");
    expect(after[0].durationMs).toBeGreaterThanOrEqual(9_000);
  });
});
