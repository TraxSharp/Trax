// AUTO-GENERATED from booking.ir.json by generateMachineFactory. Do not edit by hand.
// Imports the engine from "@traxsharp/state-machine" (trax machine generate --import-style specifier, the
// default without --engine-src). Name another module with --specifier <module>, or import an engine
// checkout by relative path with --import-style relative --engine-src <engine/src>.

import { typedMachineFromIr, type IrDocument } from "@traxsharp/state-machine";
import type { BookingSpec } from "./booking.contexts.g";

const ir = {
  "irFormat": 1,
  "committedStates": [],
  "context": {
    "Booked": {
      "fields": [
        {
          "constraints": [],
          "name": "passport",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "seat",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "CheckedIn": {
      "fields": [
        {
          "constraints": [],
          "name": "passport",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "seat",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "Issuing": {
      "fields": [
        {
          "constraints": [],
          "name": "passport",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "seat",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "Missed": {
      "fields": [
        {
          "constraints": [],
          "name": "passport",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "seat",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "NotIssued": {
      "fields": [
        {
          "constraints": [],
          "name": "passport",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "seat",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "Open": {
      "fields": [
        {
          "constraints": [],
          "name": "passport",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "seat",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "TurnedDown": {
      "fields": [
        {
          "constraints": [],
          "name": "passport",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "seat",
          "nullable": true,
          "type": "string"
        }
      ]
    }
  },
  "id": "booking",
  "initialContext": {
    "passport": "",
    "seat": null
  },
  "initialState": "Booked",
  "inputs": {
    "CheckIn": {
      "fields": [
        {
          "constraints": [],
          "name": "seat",
          "nullable": false,
          "type": "string"
        }
      ]
    },
    "GivePassport": {
      "fields": [
        {
          "constraints": [],
          "name": "passport",
          "nullable": false,
          "type": "string"
        }
      ]
    }
  },
  "outcomes": {
    "Issuing.cancelled": {
      "edges": [
        {
          "to": "NotIssued"
        }
      ],
      "outcome": "cancelled",
      "state": "Issuing"
    },
    "Issuing.done": {
      "edges": [
        {
          "guard": {
            "field": "accepted",
            "rule": "boolEquals",
            "source": "input",
            "value": true
          },
          "to": "CheckedIn"
        },
        {
          "guard": {
            "field": "accepted",
            "rule": "boolEquals",
            "source": "input",
            "value": false
          },
          "to": "TurnedDown"
        }
      ],
      "outcome": "done",
      "output": {
        "fields": [
          {
            "constraints": [],
            "name": "accepted",
            "nullable": false,
            "type": "boolean"
          }
        ]
      },
      "state": "Issuing"
    },
    "Issuing.failed": {
      "edges": [
        {
          "to": "NotIssued"
        }
      ],
      "outcome": "failed",
      "state": "Issuing"
    }
  },
  "states": [
    "Booked",
    "CheckedIn",
    "Issuing",
    "Missed",
    "NotIssued",
    "Open",
    "TurnedDown"
  ],
  "transitions": [
    {
      "from": "Booked",
      "to": "Open",
      "trigger": "OpenCheckIn"
    },
    {
      "from": "Open",
      "guard": {
        "field": "passport",
        "rule": "nonEmpty",
        "source": "context"
      },
      "guardMessage": "Add your passport number first.",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "seat",
            "value": {
              "input": "seat"
            }
          }
        ]
      },
      "to": "Issuing",
      "trigger": "CheckIn"
    },
    {
      "from": "Open",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "passport",
            "value": {
              "input": "passport"
            }
          }
        ]
      },
      "to": "Open",
      "trigger": "GivePassport"
    },
    {
      "from": "Open",
      "to": "Missed",
      "trigger": "Miss"
    }
  ],
  "triggers": [
    "CheckIn",
    "GivePassport",
    "Miss",
    "OpenCheckIn"
  ],
  "version": 1
} as IrDocument;

/** SHA-256 of this machine's IR — the version-skew handshake token (matches C#'s IMachine.SchemaHash). */
export const irHash = "35b9174b36740100773abfae429a95e6bc343c82b3a4d802a41fa48b970ca265";

/** The booking machine, built from the IR and typed by BookingSpec. No hand-written twin. */
export const booking = typedMachineFromIr<BookingSpec>(ir, irHash);
