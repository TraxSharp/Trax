// AUTO-GENERATED from order.ir.json by generateMachineFactory. Do not edit by hand.
// Imports the engine from "@traxsharp/state-machine" (trax machine generate --import-style specifier, the
// default without --engine-src). Name another module with --specifier <module>, or import an engine
// checkout by relative path with --import-style relative --engine-src <engine/src>.

import { typedMachineFromIr, type IrDocument } from "@traxsharp/state-machine";
import type { OrderSpec } from "./order.contexts.g";

const ir = {
  "irFormat": 1,
  "committedStates": [],
  "context": {
    "Baking": {
      "fields": [
        {
          "constraints": [],
          "name": "loaves",
          "nullable": false,
          "type": "integer"
        },
        {
          "constraints": [],
          "name": "approved",
          "nullable": true,
          "type": "boolean"
        }
      ]
    },
    "Declined": {
      "fields": [
        {
          "constraints": [],
          "name": "loaves",
          "nullable": false,
          "type": "integer"
        },
        {
          "constraints": [],
          "name": "approved",
          "nullable": true,
          "type": "boolean"
        }
      ]
    },
    "Draft": {
      "fields": [
        {
          "constraints": [],
          "name": "loaves",
          "nullable": false,
          "type": "integer"
        },
        {
          "constraints": [],
          "name": "approved",
          "nullable": true,
          "type": "boolean"
        }
      ]
    },
    "Paying": {
      "fields": [
        {
          "constraints": [],
          "name": "loaves",
          "nullable": false,
          "type": "integer"
        },
        {
          "constraints": [],
          "name": "approved",
          "nullable": true,
          "type": "boolean"
        }
      ]
    },
    "Placed": {
      "fields": [
        {
          "constraints": [],
          "name": "loaves",
          "nullable": false,
          "type": "integer"
        },
        {
          "constraints": [],
          "name": "approved",
          "nullable": true,
          "type": "boolean"
        }
      ]
    },
    "Ready": {
      "fields": [
        {
          "constraints": [],
          "name": "loaves",
          "nullable": false,
          "type": "integer"
        },
        {
          "constraints": [],
          "name": "approved",
          "nullable": true,
          "type": "boolean"
        }
      ]
    },
    "Unpaid": {
      "fields": [
        {
          "constraints": [],
          "name": "loaves",
          "nullable": false,
          "type": "integer"
        },
        {
          "constraints": [],
          "name": "approved",
          "nullable": true,
          "type": "boolean"
        }
      ]
    }
  },
  "id": "order",
  "initialContext": {
    "approved": null,
    "loaves": 0
  },
  "initialState": "Draft",
  "inputs": {
    "Order": {
      "fields": [
        {
          "constraints": [],
          "name": "loaves",
          "nullable": false,
          "type": "integer"
        }
      ]
    }
  },
  "outcomes": {
    "Paying.cancelled": {
      "edges": [
        {
          "to": "Unpaid"
        }
      ],
      "outcome": "cancelled",
      "state": "Paying"
    },
    "Paying.done": {
      "edges": [
        {
          "guard": {
            "field": "approved",
            "rule": "boolEquals",
            "source": "input",
            "value": true
          },
          "to": "Placed"
        },
        {
          "guard": {
            "field": "approved",
            "rule": "boolEquals",
            "source": "input",
            "value": false
          },
          "to": "Declined"
        }
      ],
      "outcome": "done",
      "output": {
        "fields": [
          {
            "constraints": [],
            "name": "approved",
            "nullable": false,
            "type": "boolean"
          }
        ]
      },
      "state": "Paying"
    },
    "Paying.failed": {
      "edges": [
        {
          "to": "Unpaid"
        }
      ],
      "outcome": "failed",
      "state": "Paying"
    }
  },
  "states": [
    "Baking",
    "Declined",
    "Draft",
    "Paying",
    "Placed",
    "Ready",
    "Unpaid"
  ],
  "timers": [
    {
      "after": "PT10S",
      "rules": [
        "undeclared"
      ],
      "state": "Baking",
      "target": "Ready",
      "trigger": "Baking.after"
    }
  ],
  "transitions": [
    {
      "from": "Draft",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "loaves",
            "value": {
              "input": "loaves"
            }
          }
        ]
      },
      "to": "Draft",
      "trigger": "Order"
    },
    {
      "from": "Draft",
      "guard": {
        "field": "loaves",
        "op": "gt",
        "rule": "compare",
        "source": "context",
        "value": 0
      },
      "guardMessage": "Add something first.",
      "to": "Paying",
      "trigger": "Place"
    },
    {
      "from": "Placed",
      "to": "Baking",
      "trigger": "Bake"
    }
  ],
  "triggers": [
    "Bake",
    "Order",
    "Place"
  ],
  "version": 1
} as IrDocument;

/** SHA-256 of this machine's IR — the version-skew handshake token (matches C#'s IMachine.SchemaHash). */
export const irHash = "b33a6c162e153ed992f1e766efcc16074087e5d8de90482b5f03086d94716725";

/** The order machine, built from the IR and typed by OrderSpec. No hand-written twin. */
export const order = typedMachineFromIr<OrderSpec>(ir, irHash);
