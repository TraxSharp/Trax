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
    "Cancelled": {
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
          "nullable": true,
          "type": "array",
          "values": {
            "nullable": false,
            "type": "string"
          }
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "total",
          "nullable": true,
          "type": "number"
        }
      ]
    },
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
          "nullable": true,
          "type": "array",
          "values": {
            "nullable": false,
            "type": "string"
          }
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "total",
          "nullable": true,
          "type": "number"
        }
      ]
    },
    "Cleared": {
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
          "nullable": true,
          "type": "array",
          "values": {
            "nullable": false,
            "type": "string"
          }
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "total",
          "nullable": true,
          "type": "number"
        }
      ]
    },
    "Delivered": {
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
          "nullable": true,
          "type": "array",
          "values": {
            "nullable": false,
            "type": "string"
          }
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "total",
          "nullable": true,
          "type": "number"
        }
      ]
    },
    "Ordered": {
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
          "nullable": true,
          "type": "array",
          "values": {
            "nullable": false,
            "type": "string"
          }
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "total",
          "nullable": true,
          "type": "number"
        }
      ]
    }
  },
  "id": "order",
  "initialContext": {},
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
    }
  },
  "outcomes": {
    "Ordered.cancelled": {
      "edges": [
        {
          "to": "Cancelled"
        }
      ],
      "outcome": "cancelled",
      "state": "Ordered"
    },
    "Ordered.done": {
      "edges": [
        {
          "to": "Delivered"
        }
      ],
      "outcome": "done",
      "output": {
        "fields": [
          {
            "constraints": [],
            "name": "courier",
            "nullable": false,
            "type": "string"
          },
          {
            "constraints": [],
            "name": "orderId",
            "nullable": false,
            "type": "string"
          }
        ]
      },
      "state": "Ordered"
    },
    "Ordered.failed": {
      "edges": [
        {
          "to": "Cancelled"
        }
      ],
      "outcome": "failed",
      "state": "Ordered"
    }
  },
  "states": [
    "Cancelled",
    "Cart",
    "Cleared",
    "Delivered",
    "Ordered"
  ],
  "timers": [
    {
      "after": "PT30M",
      "rules": [
        "AnyAuthenticated"
      ],
      "state": "Cart",
      "target": "Cleared",
      "trigger": "Cart.after"
    }
  ],
  "transitions": [
    {
      "from": "Cart",
      "guard": {
        "field": "item",
        "rule": "oneOf",
        "source": "input",
        "values": [
          "Margherita",
          "Pepperoni",
          "Garlic bread"
        ]
      },
      "guardMessage": "That is not on the menu.",
      "reduce": {
        "name": "order:add-item",
        "reduce": "custom"
      },
      "to": "Cart",
      "trigger": "AddItem"
    },
    {
      "from": "Cart",
      "guard": {
        "field": "items",
        "op": "gte",
        "rule": "count",
        "source": "context",
        "value": 1
      },
      "guardMessage": "The cart is empty.",
      "to": "Ordered",
      "trigger": "PlaceOrder"
    }
  ],
  "triggers": [
    "AddItem",
    "PlaceOrder"
  ],
  "version": 1
} as IrDocument;

/** SHA-256 of this machine's IR — the version-skew handshake token (matches C#'s IMachine.SchemaHash). */
export const irHash = "40b0d0b397343171eec0405299edc770437b0cccf83a818e7b71d73421efcd32";

/** The order machine, built from the IR and typed by OrderSpec. No hand-written twin. */
export const order = typedMachineFromIr<OrderSpec>(ir, irHash);
