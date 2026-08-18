// AUTO-GENERATED from turnstile.ir.json by generateMachineFactory. Do not edit by hand.

import { typedMachineFromIr } from "../../typed";
import type { IrDocument } from "../../rules/irMachine";
import type { TurnstileSpec } from "./turnstile.contexts.g";

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

/** SHA-256 of this machine's IR — the version-skew handshake token (matches C#'s IMachine.SchemaHash). */
export const irHash = "3616328b5f265cb5e7c0c4605df513407e5b88c6a32d1ef9b86c29779201d5e2";

/** The turnstile machine, built from the IR and typed by TurnstileSpec. No hand-written twin. */
export const turnstile = typedMachineFromIr<TurnstileSpec>(ir, irHash);
