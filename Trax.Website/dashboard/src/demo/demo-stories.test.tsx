import type { ComponentType } from "react";
import { composeStories } from "@storybook/react-vite";
import { cleanup, render } from "@testing-library/react";
import { configure } from "@testing-library/dom";
import { configure as configureStorybook } from "storybook/test";
import { afterAll, afterEach, beforeAll, describe, expect, test } from "vitest";
import { hashVariables } from "../mock/variables-hash";
import type { Served } from "./lookup";
import { notOfferedYet, recordings, settle } from "./harness";
import { setStoryRecordings } from "./story-source";

// Every story, with its play function, on the demo's recordings instead of the auto-mock. The plays
// were written against the mock's scenarios (an AlphaJob, a page of three), so on recorded data many
// of their assertions do not hold, and that is not what this checks. What it checks is the demo's
// promise: every query a story's page sends, before and after each interaction, is one the
// recordings answer, so no page in the demo can fall back to invented data.
//
// Two answers are counted rather than failed, because a story chooses them, not the demo: a story
// pinned to a scenario's row the recordings do not have (the mock's run 9001, or a "not found"
// story's missing id), and a filter a play types (answered "derived", as a visitor's typing is).
// Everything else a story's page sends must be recorded.

type StoryModule = Parameters<typeof composeStories>[0];
type PlayFn = (ctx: { canvasElement: HTMLElement }) => Promise<void> | void;
type ComposedStory = ComponentType & { parameters?: { real?: boolean }; play?: PlayFn };

const modules = import.meta.glob("../**/*.stories.tsx", { eager: true });
const ledger = new Map<string, { story: string; op: string; variables: Record<string, unknown>; served: Served }>();
let current = "";
const plays = { passed: 0, failed: 0 };

beforeAll(() => {
  setStoryRecordings({
    recordings: recordings(),
    onServe: (op, variables, served) => {
      const key = `${op} ${hashVariables(variables)}`;
      const known = ledger.get(key);
      if (!known || known.served === "recorded") ledger.set(key, { story: current, op, variables, served });
    },
  });
  // Assertions written for the mock fail fast here rather than wait out the suite's 5s.
  configure({ asyncUtilTimeout: 300 });
  configureStorybook({ asyncUtilTimeout: 300 });
});

afterAll(() => {
  setStoryRecordings(null);
  configure({ asyncUtilTimeout: 5000 });
  configureStorybook({ asyncUtilTimeout: 5000 });
});

afterEach(cleanup);

// The variables that name one row.
const ROW_VARIABLES = ["id", "metadataId", "parentId", "manifestId", "manifestGroupId", "trainName"];

/** True when the query names a row (by id or train) that no recording of any query names. */
function scenarioRow(variables: Record<string, unknown>): boolean {
  const named = ROW_VARIABLES.filter((k) => variables[k] != null);
  if (named.length === 0) return false;
  const known = new Set<string>();
  for (const byHash of Object.values(recordings().queries))
    for (const key of Object.keys(byHash)) {
      const vars = JSON.parse(key) as Record<string, unknown>;
      for (const k of ROW_VARIABLES) if (vars[k] != null) known.add(`${k}=${vars[k]}`);
    }
  return named.some((k) => !known.has(`${k}=${variables[k]}`));
}

describe("stories on the recordings", () => {
  for (const [path, mod] of Object.entries(modules)) {
    const composed = composeStories(mod as StoryModule);
    for (const [name, Story] of Object.entries(composed) as [string, ComposedStory][]) {
      if (Story.parameters?.real) continue;
      // The glob's paths start with "../"; the label drops that prefix only.
      const label = `${path.replace(/^\.\.\//, "")} ${name}`;
      test(label, async () => {
        current = label;
        const { container } = render(<Story />);
        await settle();
        try {
          if (Story.play) await Story.play({ canvasElement: container });
          plays.passed++;
        } catch {
          plays.failed++;
        }
        await settle();
      });
    }
  }

  test("every query the stories sent was answered by a recording", () => {
    // A story of a page the demo does not offer yet (harness.tsx, NOT_RECORDED_YET) sends reads no
    // recording answers; the demo never sends them.
    const entries = [...ledger.values()].filter((e) => !(e.served === "missing" && notOfferedYet(e.op)));
    const scenario = entries.filter((e) => e.served === "created" || (e.served === "missing" && scenarioRow(e.variables)));
    const typed = entries.filter((e) => e.served === "derived");
    const missing = entries.filter((e) => e.served === "missing" && !scenarioRow(e.variables));
    process.stdout.write(
      `stories on recordings: ${entries.length} distinct queries, ${entries.length - scenario.length - typed.length - missing.length} recorded, ` +
        `${typed.length} typed filters derived, ${scenario.length} for a scenario's own row; ` +
        `plays ${plays.passed} held, ${plays.failed} assumed mock data\n`,
    );
    expect(missing.map((e) => `${e.op} ${JSON.stringify(e.variables)} (${e.story})`)).toEqual([]);
  });
});
