// AUTO-GENERATED from turnstile.ir.json by generateMachineFactory. Do not edit by hand.
// Imports the engine from "@traxsharp/state-machine" (trax machine generate --import-style specifier, the
// default without --engine-src). Name another module with --specifier <module>, or import an engine
// checkout by relative path with --import-style relative --engine-src <engine/src>.

import { typedMachineFromIr, type IrDocument } from "@traxsharp/state-machine";
import type { TurnstileSpec } from "./turnstile.contexts.g";

const ir = {
  "irFormat": 1,
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
export const irHash = "4bf6af4201ab2b07c86add8b4efa928bb69aea44599f706dbd8cba0c7cc19486";

/** The turnstile machine, built from the IR and typed by TurnstileSpec. No hand-written twin. */
export const turnstile = typedMachineFromIr<TurnstileSpec>(ir, irHash);
