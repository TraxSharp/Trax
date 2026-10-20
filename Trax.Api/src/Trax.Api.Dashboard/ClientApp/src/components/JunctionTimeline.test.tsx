import { act, render, screen, within } from "@testing-library/react";
import { expect, test } from "vitest";
import { Provider, type Client } from "urql";
import { JunctionTimeline } from "./JunctionTimeline";
import type { JunctionEvent, JunctionStep } from "../types";

function step(state: JunctionStep["state"], endedAt: string | null): JunctionStep {
  return {
    position: 0,
    kind: "JUNCTION",
    name: "WaitForLedger",
    state,
    startedAt: new Date(0).toISOString(),
    endedAt,
    durationMs: endedAt ? 1000 : null,
    failureClass: null,
    failureException: null,
    questionKey: null,
    answer: null,
    confidence: null,
    replayed: false,
    decider: null,
    answerWithheld: false,
    attempt: null,
    nameWithheld: false,
    trackPosition: null,
  };
}

// A client whose reads resolve when the test says, and whose step feed the test pushes into, so a
// read and an event can be made to arrive in either order.
function fakeClient() {
  const reads: ((steps: JunctionStep[]) => void)[] = [];
  let push: ((r: { data: { onJunctionEvent: JunctionEvent } }) => void) | null = null;
  const client = {
    query: () => ({
      toPromise: () =>
        new Promise((resolve) =>
          reads.push((steps) => resolve({ data: { operations: { junctionRuns: steps } } })),
        ),
    }),
    subscription: () => ({
      subscribe: (next: typeof push) => {
        push = next;
        return { unsubscribe: () => {} };
      },
    }),
  } as unknown as Client;
  return {
    client,
    answerRead: (steps: JunctionStep[]) => act(async () => reads.shift()!(steps)),
    pushStep: (s: JunctionStep, sequence: number) =>
      act(() =>
        push!({
          data: {
            onJunctionEvent: {
              metadataId: 7,
              externalId: "e7",
              trainName: "T",
              eventType: "JUNCTION_COMPLETED",
              timestamp: new Date(0).toISOString(),
              junction: s,
              sequence,
            } satisfies JunctionEvent,
          },
        }),
      ),
  };
}

test("a step that ends while the first read is in flight is not undone by that read", async () => {
  const { client, answerRead, pushStep } = fakeClient();
  render(
    <Provider value={client}>
      <JunctionTimeline
        metadataId={7}
        startTime={new Date(0).toISOString()}
        endTime={null}
        trainState="IN_PROGRESS"
      />
    </Provider>,
  );

  // The step ends and is pushed before the first read, taken while it ran, comes back.
  await pushStep(step("COMPLETED", new Date(1000).toISOString()), 1);
  await answerRead([step("IN_PROGRESS", null)]);

  const timeline = screen.getByRole("region", { name: "Junction timeline" });
  expect(within(timeline).getAllByTitle(/^COMPLETED/).length).toBeGreaterThan(0);
});
