import { describe, expect, it } from "vitest";
import { enumerate, serializeCorpus, type DifferentialSpec } from "@traxsharp/state-machine";
import ir from "./topic-map.ir.json";
import committed from "./differential.json?raw";
import { topicMap } from "./topic-map.machine.g";

// The twin is the oracle: it enumerates the topic map's differential block from the committed IR and must
// produce the committed corpus byte for byte. The C# engine replays the same file in the Recovery tests
// (TopicMapMachine_reproduces_its_committed_differential_corpus), so the two runtimes agree on every case.
// Both files are written by `trax machine generate --corpus-out`, with the command in the sample's README.
describe("the topic map's differential corpus", () => {
  it("TopicMapMachine reproduces its committed differential corpus", () => {
    const generated = serializeCorpus(enumerate(topicMap.core, ir as unknown as DifferentialSpec));

    expect(
      generated,
      "the topic map's differential.json is stale: regenerate it with the command in the sample's README",
    ).toBe(committed);
  });
});
