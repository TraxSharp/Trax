// The sample's real C#, imported from the sibling project at build time, so the code panels always show what
// actually runs.
import orderMachine from "../../Trax.Samples.Recovery/Machines/OrderMachine.cs?raw";
import deliverOrderTrain from "../../Trax.Samples.Recovery/Trains/Order/DeliverOrderTrain.cs?raw";
import refundTrain from "../../Trax.Samples.Recovery/Trains/Refund/ApproveRefundTrain.cs?raw";
import topicMapTrain from "../../Trax.Samples.Recovery/Trains/Topics/BuildTopicMapTrain.cs?raw";
import type { Scenario, Step } from "./types";

export interface Source {
  file: string;
  code: string;
}

const source = (file: string, code: string): Source => ({ file, code: code.replace(/\r/g, "") });

export const SOURCES: Record<Scenario, Source> = {
  REFUND: source("Trains/Refund/ApproveRefundTrain.cs", refundTrain),
  TOPIC_MAP: source("Trains/Topics/BuildTopicMapTrain.cs", topicMapTrain),
};

export const ORDER_SOURCES = {
  machine: source("Machines/OrderMachine.cs", orderMachine),
  train: source("Trains/Order/DeliverOrderTrain.cs", deliverOrderTrain),
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

/**
 * The line (1-based) to light for each part of the order machine, read from its source. `state:Cart` and
 * `state:Ordered` are their m.In lines; an end state is the line that sends the order there (`state:Delivered` the
 * .OnDone, `state:Cancelled` the .OnFailed, `state:Cleared` the timer's .GoTo). `event:<operation>` is the trigger's
 * .On line, `event:start` the .Start line, `invoke` the .Invokes line and `timer` the .After line.
 */
export function orderLines(code: string): Record<string, number> {
  const lines = code.split("\n");
  const find = (needle: string) => lines.findIndex((l) => l.includes(needle)) + 1;
  const out: Record<string, number> = {};
  for (const state of ["Cart", "Ordered"]) out[`state:${state}`] = find(`m.In(OrderState.${state})`);
  out["state:Delivered"] = find(".OnDone(OrderState.Delivered");
  out["state:Cancelled"] = find(".OnFailed(OrderState.Cancelled");
  out["state:Cleared"] = find(".GoTo(OrderState.Cleared");
  out["event:addItem"] = find(".On(OrderTrigger.AddItem)");
  out["event:placeOrder"] = find(".On(OrderTrigger.PlaceOrder)");
  out["event:start"] = find(".Start<OrderStart>");
  out.invoke = find(".Invokes<IDeliverOrderTrain");
  out.timer = find(".After(");
  return out;
}
