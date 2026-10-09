// The trains' real C#, imported from the sibling project at build time, so the code panel always
// shows what actually runs.
import researchTrain from "../../Trax.Samples.Recovery/Trains/Research/ResearchTopicTrain.cs?raw";
import refundTrain from "../../Trax.Samples.Recovery/Trains/Refund/ApproveRefundTrain.cs?raw";
import topicMapTrain from "../../Trax.Samples.Recovery/Trains/Topics/BuildTopicMapTrain.cs?raw";
import type { Scenario, Step } from "./types";

export const SOURCES: Record<Scenario, { file: string; code: string }> = {
  RESEARCH: { file: "Trains/Research/ResearchTopicTrain.cs", code: researchTrain },
  REFUND: { file: "Trains/Refund/ApproveRefundTrain.cs", code: refundTrain },
  TOPIC_MAP: { file: "Trains/Topics/BuildTopicMapTrain.cs", code: topicMapTrain },
};

const GATE_TRACKS: Record<string, string> = { Yes: ".Yes(", No: ".No(", Unsure: ".Unsure(" };

/**
 * The line (0-based) of the train's source that declares a step: `Chain<Name>` for a junction, the
 * routing step for a question, and the track's `.When(...)`, `.AtLeast(...)` or `.Yes(...)` for a
 * route. -1 when the step is withheld or not found.
 */
export function lineOf(code: string, step: Step): number {
  const lines = code.split("\n");
  const find = (needle: string) => lines.findIndex((l) => l.includes(needle));
  if (step.nameWithheld) return -1;

  switch (step.kind) {
    case "JUNCTION":
      return find(`Chain<${step.name}>`);
    case "ROUTE": {
      const gate = step.answer ? GATE_TRACKS[step.answer] : undefined;
      if (gate) return find(gate);
      return find(`${step.questionKey}.${step.answer}`);
    }
    default:
      return find(`, ${step.questionKey}>`);
  }
}
