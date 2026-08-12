import { describe, expect, it } from "vitest";
import fs from "node:fs";
import { SnapshotMachine } from "../../machine";
import type { MachineDefinition } from "../../types";
import { migrationFile } from "../../fixtures";
import {
  turnstileCore,
  turnstileDefinition,
  type TurnstileState,
  type TurnstileTrigger,
} from "./turnstile";

const v2Definition: MachineDefinition<TurnstileState, TurnstileTrigger> = {
  ...turnstileDefinition,
  version: 2,
  migrations: {
    1: (state, ctx) => ({ state, context: { ...ctx, migrated: true } }),
  },
  contextValidators: {
    ...turnstileDefinition.contextValidators,
    // v2's Unlocked context additionally carries `migrated`; the v1 schema validator (which forbids extra
    // fields) is overridden here for the new shape.
    Unlocked: (ctx) =>
      typeof ctx.paidWith === "string" && ctx.paidWith.length > 0
        ? null
        : "Unlocked requires a non-empty paidWith.",
  },
};
const turnstileV2 = new SnapshotMachine(v2Definition);

const V1_UNLOCKED =
  '{"machine":"turnstile","version":1,"state":"Unlocked","context":{"paidWith":"quarter"}}';

describe("turnstile migration", () => {
  it("migrates an older snapshot forward on rehydrate", () => {
    const result = turnstileV2.rehydrate(V1_UNLOCKED);
    expect(result.result).toBe("ok");
    if (result.result === "ok") {
      expect(result.snapshot.version).toBe(2);
      expect(result.snapshot.context).toEqual({
        paidWith: "quarter",
        migrated: true,
      });
    }
  });

  it("rejects a gap in the migration chain as version-mismatch", () => {
    const v3WithGap = new SnapshotMachine<TurnstileState, TurnstileTrigger>({
      ...turnstileDefinition,
      version: 3,
      migrations: { 1: (state, ctx) => ({ state, context: { ...ctx } }) }, // no 2 -> 3
    });
    const result = v3WithGap.rehydrate(V1_UNLOCKED);
    expect(result.result).toBe("error");
    if (result.result === "error") expect(result.code).toBe("version-mismatch");
  });

  it("rejects a snapshot newer than the definition as version-mismatch", () => {
    const result = turnstileCore.rehydrate(
      '{"machine":"turnstile","version":2,"state":"Locked","context":{}}',
    );
    expect(result.result).toBe("error");
    if (result.result === "error") expect(result.code).toBe("version-mismatch");
  });
});

// The migration golden guards correctness against real stored shapes: each stored older-version snapshot must
// forward-migrate to the exact committed canonical wire, so a dropped/renamed/reordered field fails. The C#
// engine replays the SAME file (MigrationGoldenTests), so the two runtimes' migrations cannot diverge.
describe("turnstile migration golden", () => {
  const golden = JSON.parse(
    fs.readFileSync(migrationFile("turnstile"), "utf8"),
  ) as {
    cases: { name: string; stored: string; expected: string }[];
  };

  it.each(golden.cases)("$name", ({ stored, expected }) => {
    const result = turnstileV2.rehydrate(stored);
    expect(result.result).toBe("ok");
    if (result.result === "ok") {
      expect(turnstileV2.serialize(result.snapshot)).toBe(expected);
    }
  });
});
