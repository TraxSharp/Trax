import { act, render, screen, within } from "@testing-library/react";
import { expect, test } from "vitest";
import { ConsoleClient, ConsoleClientProvider } from "../lib/consoleClient";
import type { TraxTransport } from "../trax/index.g";
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
  const transport: TraxTransport = {
    execute: () =>
      new Promise((resolve) => reads.push((steps) => resolve({ data: { operations: { junctionRuns: steps } } }))),
    subscribe: (_request, sink) => {
      push = (answer) => sink.next(answer);
      return () => {};
    },
  };
  const client = new ConsoleClient(transport);
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
    <ConsoleClientProvider client={client}>
      <JunctionTimeline
        metadataId={7}
        startTime={new Date(0).toISOString()}
        endTime={null}
        trainState="IN_PROGRESS"
      />
    </ConsoleClientProvider>,
  );

  // The step ends and is pushed before the first read, taken while it ran, comes back.
  await pushStep(step("COMPLETED", new Date(1000).toISOString()), 1);
  await answerRead([step("IN_PROGRESS", null)]);

  const timeline = screen.getByRole("region", { name: "Junction timeline" });
  expect(within(timeline).getAllByTitle(/^COMPLETED/).length).toBeGreaterThan(0);
});
