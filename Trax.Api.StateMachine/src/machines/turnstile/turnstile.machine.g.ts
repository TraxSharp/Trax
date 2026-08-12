// AUTO-GENERATED from turnstile.ir.json by generateMachineFactory. Do not edit by hand.

import { SnapshotMachine } from "../../machine";
import { machineFromIr, type IrDocument } from "../../rules/irMachine";
import { TypedMachine } from "../../typed";
import type { TurnstileSpec, TurnstileState, TurnstileTrigger } from "./turnstile.contexts.g";

const ir = {
  "id": "turnstile",
  "version": 1,
  "initialState": "Locked",
  "states": [
    "Locked",
    "Unlocked"
  ],
  "triggers": [
    "Coin",
    "Push"
  ],
  "committedStates": [],
  "context": {
    "Locked": {
      "fields": []
    },
    "Unlocked": {
      "fields": [
        {
          "name": "paidWith",
          "type": "string",
          "nullable": false,
          "constraints": [
            {
              "rule": "nonEmpty",
              "source": "context",
              "field": "paidWith"
            }
          ]
        }
      ]
    }
  },
  "inputs": {
    "Coin": {
      "fields": [
        {
          "name": "coin",
          "type": "string",
          "nullable": false,
          "constraints": []
        }
      ]
    }
  },
  "transitions": [
    {
      "from": "Locked",
      "trigger": "Coin",
      "to": "Unlocked",
      "guard": {
        "rule": "oneOf",
        "source": "input",
        "field": "coin",
        "values": [
          "quarter",
          "dollar"
        ]
      },
      "guardMessage": "Only a quarter or a dollar is accepted.",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "paidWith",
            "value": {
              "input": "coin"
            }
          }
        ]
      }
    },
    {
      "from": "Unlocked",
      "trigger": "Push",
      "to": "Locked",
      "reduce": {
        "reduce": "clear"
      }
    }
  ]
} as IrDocument;

// The runtime machine's states/triggers are exactly those in the IR; the cast narrows the string
// generics to the generated unions so TypedMachine<TurnstileSpec> lines up.
const core = new SnapshotMachine(machineFromIr(ir)) as unknown as SnapshotMachine<
  TurnstileState,
  TurnstileTrigger
>;

/** The turnstile machine, built from the IR and typed by TurnstileSpec. No hand-written twin. */
export const turnstile = new TypedMachine<TurnstileSpec>(core);
