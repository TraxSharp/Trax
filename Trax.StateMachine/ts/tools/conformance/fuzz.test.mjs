import { beforeAll, describe, expect, it } from "vitest";
import { fuzz } from "../../../conformance/fuzz.mjs";
import { createTestee, loadEngine } from "./testee-ts.mjs";

// The differential fuzzer over the TypeScript engine twice: two engines that answer alike pass, and two that both
// answer an error on an operation the protocol makes total fail, though they agree.

let engine;

beforeAll(async () => {
  engine = await loadEngine();
}, 60_000);

const testee = (name, change = (_request, answer) => answer) => {
  const handle = createTestee(engine);
  return { name, exchange: async (request) => change(request, handle(request)) };
};

describe("the differential fuzzer", () => {
  it("passes two engines that answer alike", async () => {
    expect(await fuzz({ seed: 1, count: 200, irs: true, testees: [testee("a"), testee("b")] })).toBeNull();
  });

  it("fails two engines that both answer an advance with an error", async () => {
    const throws = (request, answer) =>
      request.op === "advance" ? { id: request.id, error: "TypeError: cannot read properties of undefined" } : answer;
    const failure = await fuzz({
      seed: 1,
      count: 200,
      irs: false,
      testees: [testee("a", throws), testee("b", throws)],
    });

    expect(failure).not.toBeNull();
    expect(failure.request.op).toBe("advance");
    expect(failure.answers.map(([, answer]) => typeof answer.error)).toEqual(["string", "string"]);
  });

  it("passes two engines that both refuse to load a random machine", async () => {
    const refuses = (request, answer) =>
      request.op === "load" && request.machine.startsWith("fuzz") ? { id: request.id, error: "refused" } : answer;
    expect(
      await fuzz({ seed: 1, count: 200, irs: true, testees: [testee("a", refuses), testee("b", refuses)] }),
    ).toBeNull();
  });
});
