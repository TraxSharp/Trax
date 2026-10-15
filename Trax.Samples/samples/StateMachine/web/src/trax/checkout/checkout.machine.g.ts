// AUTO-GENERATED from checkout.ir.json by generateMachineFactory. Do not edit by hand.
// Imports the engine from "@traxsharp/state-machine" (trax machine generate --import-style specifier, the
// default without --engine-src). Name another module with --specifier <module>, or import an engine
// checkout by relative path with --import-style relative --engine-src <engine/src>.

import { typedMachineFromIr, type IrDocument } from "@traxsharp/state-machine";
import type { CheckoutSpec } from "./checkout.contexts.g";

const ir = {
  "irFormat": 1,
  "committedStates": [
    "Paid"
  ],
  "context": {
    "Cart": {
      "fields": [
        {
          "constraints": [
            {
              "field": "items",
              "rule": "arrayOf",
              "source": "context",
              "type": "string"
            }
          ],
          "name": "items",
          "nullable": false,
          "type": "array",
          "values": {
            "nullable": false,
            "type": "string"
          }
        },
        {
          "constraints": [],
          "name": "receipt",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 9223372036854775000,
          "minimum": -9223372036854775000,
          "name": "total",
          "nullable": false,
          "type": "integer"
        }
      ]
    },
    "Paid": {
      "fields": [
        {
          "constraints": [
            {
              "field": "items",
              "rule": "arrayOf",
              "source": "context",
              "type": "string"
            }
          ],
          "name": "items",
          "nullable": false,
          "type": "array",
          "values": {
            "nullable": false,
            "type": "string"
          }
        },
        {
          "constraints": [],
          "name": "receipt",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 9223372036854775000,
          "minimum": -9223372036854775000,
          "name": "total",
          "nullable": false,
          "type": "integer"
        }
      ]
    },
    "Review": {
      "fields": [
        {
          "constraints": [
            {
              "field": "items",
              "rule": "arrayOf",
              "source": "context",
              "type": "string"
            }
          ],
          "name": "items",
          "nullable": false,
          "type": "array",
          "values": {
            "nullable": false,
            "type": "string"
          }
        },
        {
          "constraints": [],
          "name": "receipt",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 9223372036854775000,
          "minimum": -9223372036854775000,
          "name": "total",
          "nullable": false,
          "type": "integer"
        }
      ]
    }
  },
  "id": "checkout",
  "initialContext": {
    "items": [],
    "receipt": null,
    "total": 0
  },
  "initialState": "Cart",
  "inputs": {
    "AddItem": {
      "fields": [
        {
          "constraints": [],
          "name": "item",
          "nullable": false,
          "type": "string"
        }
      ]
    },
    "Pay": {
      "fields": [
        {
          "constraints": [],
          "name": "receipt",
          "nullable": false,
          "type": "string"
        }
      ]
    }
  },
  "invariants": {
    "Cart": {
      "rule": "all",
      "rules": [
        {
          "field": "receipt",
          "rule": "absent",
          "source": "context"
        },
        {
          "name": "checkout:total-matches-items",
          "rule": "custom"
        }
      ]
    },
    "Paid": {
      "rule": "all",
      "rules": [
        {
          "field": "items",
          "op": "gt",
          "rule": "count",
          "source": "context",
          "value": 0
        },
        {
          "field": "receipt",
          "rule": "nonEmpty",
          "source": "context"
        },
        {
          "name": "checkout:total-matches-items",
          "rule": "custom"
        }
      ]
    },
    "Review": {
      "rule": "all",
      "rules": [
        {
          "field": "items",
          "op": "gt",
          "rule": "count",
          "source": "context",
          "value": 0
        },
        {
          "field": "receipt",
          "rule": "absent",
          "source": "context"
        },
        {
          "name": "checkout:total-matches-items",
          "rule": "custom"
        }
      ]
    }
  },
  "states": [
    "Cart",
    "Paid",
    "Review"
  ],
  "transitions": [
    {
      "from": "Cart",
      "guard": {
        "field": "item",
        "rule": "nonEmpty",
        "source": "input"
      },
      "guardMessage": "Name the item to add.",
      "reduce": {
        "name": "checkout:add-item",
        "reduce": "custom"
      },
      "to": "Cart",
      "trigger": "AddItem"
    },
    {
      "from": "Cart",
      "guard": {
        "field": "items",
        "op": "gt",
        "rule": "count",
        "source": "context",
        "value": 0
      },
      "guardMessage": "Add an item before reviewing.",
      "to": "Review",
      "trigger": "Next"
    },
    {
      "from": "Paid",
      "reduce": {
        "reduce": "reset"
      },
      "to": "Cart",
      "trigger": "Reset"
    },
    {
      "from": "Review",
      "to": "Cart",
      "trigger": "Back"
    },
    {
      "effect": {
        "keyPrefix": "checkout:charge"
      },
      "from": "Review",
      "guard": {
        "rule": "all",
        "rules": [
          {
            "field": "items",
            "op": "gt",
            "rule": "count",
            "source": "context",
            "value": 0
          },
          {
            "field": "receipt",
            "rule": "nonEmpty",
            "source": "input"
          }
        ]
      },
      "guardMessage": "Checkout needs items and a receipt to be paid.",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "receipt",
            "value": {
              "input": "receipt"
            }
          }
        ]
      },
      "to": "Paid",
      "trigger": "Pay"
    }
  ],
  "triggers": [
    "AddItem",
    "Back",
    "Next",
    "Pay",
    "Reset"
  ],
  "version": 2
} as IrDocument;

/** SHA-256 of this machine's IR — the version-skew handshake token (matches C#'s IMachine.SchemaHash). */
export const irHash = "523826163da0b1179f841018973b138e5c19666a8a83978e93a5e82eb2a96bd9";

/** The checkout machine, built from the IR and typed by CheckoutSpec. No hand-written twin. */
export const checkout = typedMachineFromIr<CheckoutSpec>(ir, irHash);
