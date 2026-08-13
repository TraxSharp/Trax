// AUTO-GENERATED from turnstile.ir.json by generateMachineFactory. Do not edit by hand.

import { SnapshotMachine } from "../../machine";
import { machineFromIr, type IrDocument } from "../../rules/irMachine";
import { TypedMachine } from "../../typed";
import type { TurnstileSpec, TurnstileState, TurnstileTrigger } from "./turnstile.contexts.g";

const ir = {
  "committedStates": [],
  "context": {
    "Locked": {
      "fields": []
    },
    "Unlocked": {
      "fields": [
        {
          "constraints": [
            {
              "field": "paidWith",
              "rule": "nonEmpty",
              "source": "context"
            }
          ],
          "name": "paidWith",
          "nullable": false,
          "type": "string"
        }
      ]
    }
  },
  "id": "turnstile",
  "initialContext": {},
  "initialState": "Locked",
  "inputs": {
    "Coin": {
      "fields": [
        {
          "constraints": [],
          "name": "coin",
          "nullable": false,
          "type": "string"
        }
      ]
    }
  },
  "states": [
    "Locked",
    "Unlocked"
  ],
  "transitions": [
    {
      "from": "Locked",
      "guard": {
        "field": "coin",
        "rule": "oneOf",
        "source": "input",
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
      },
      "to": "Unlocked",
      "trigger": "Coin"
    },
    {
      "from": "Unlocked",
      "reduce": {
        "reduce": "clear"
      },
      "to": "Locked",
      "trigger": "Push"
    }
  ],
  "triggers": [
    "Coin",
    "Push"
  ],
  "version": 1
} as IrDocument;

// The runtime machine's states/triggers are exactly those in the IR; the cast narrows the string
// generics to the generated unions so TypedMachine<TurnstileSpec> lines up.
const core = new SnapshotMachine(machineFromIr(ir)) as unknown as SnapshotMachine<
  TurnstileState,
  TurnstileTrigger
>;

/** The turnstile machine, built from the IR and typed by TurnstileSpec. No hand-written twin. */
export const turnstile = new TypedMachine<TurnstileSpec>(core);
