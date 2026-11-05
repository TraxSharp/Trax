import { describe, expect, it, vi } from "vitest";

// The generated followRunProgress over a socket the test hands frames to.
const socket = vi.hoisted(() => ({
  sink: null as null | { next: (result: unknown) => void; complete: () => void },
  disposed: 0,
}));
vi.mock("./trax/client.g", async (original) => ({
  ...(await original<typeof import("./trax/client.g")>()),
  traxSocket: () => ({
    subscribe: (_payload: unknown, sink: NonNullable<typeof socket.sink>) => {
      socket.sink = sink;
      return () => {
        socket.disposed += 1;
      };
    },
  }),
}));

const { followRunProgress } = await import("./trax/progress.g");
const { TraxOperationError } = await import("./trax/client.g");

describe("followRunProgress", () => {
  it.each([{ data: null }, { data: null, errors: [] }, {}])(
    "ends the stream once, with a failure, on a frame with neither data nor an error: %j",
    (frame) => {
      socket.disposed = 0;
      const events: unknown[] = [];
      const ends: unknown[] = [];
      followRunProgress(
        "run-1",
        (event) => events.push(event),
        (error) => ends.push(error),
      );

      socket.sink!.next(frame);
      socket.sink!.complete();

      expect(events).toEqual([]);
      expect(ends).toHaveLength(1);
      expect(ends[0]).toBeInstanceOf(TraxOperationError);
      expect(socket.disposed).toBe(1);
    },
  );
});
