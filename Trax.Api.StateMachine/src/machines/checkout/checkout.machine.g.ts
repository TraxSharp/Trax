// AUTO-GENERATED from checkout.ir.json by generateMachineFactory. Do not edit by hand.

import { typedMachineFromIr } from "../../typed";
import type { IrDocument } from "../../rules/irMachine";
import type { CheckoutSpec } from "./checkout.contexts.g";

const ir = {
  "committedStates": [
    "Paid"
  ],
  "context": {
    "Cart": {
      "fields": [
        {
          "constraints": [],
          "name": "currency",
          "nullable": false,
          "type": "string"
        },
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
          "type": "array"
        },
        {
          "constraints": [],
          "name": "receipt",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "total",
          "nullable": false,
          "type": "number"
        }
      ]
    },
    "Paid": {
      "fields": [
        {
          "constraints": [],
          "name": "currency",
          "nullable": false,
          "type": "string"
        },
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
          "type": "array"
        },
        {
          "constraints": [],
          "name": "receipt",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "total",
          "nullable": false,
          "type": "number"
        }
      ]
    },
    "Review": {
      "fields": [
        {
          "constraints": [],
          "name": "currency",
          "nullable": false,
          "type": "string"
        },
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
          "type": "array"
        },
        {
          "constraints": [],
          "name": "receipt",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "total",
          "nullable": false,
          "type": "number"
        }
      ]
    }
  },
  "id": "checkout",
  "initialContext": {
    "currency": "USD",
    "items": [],
    "receipt": null,
    "total": 0
  },
  "initialState": "Cart",
  "inputs": {
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
      "field": "receipt",
      "rule": "absent",
      "source": "context"
    },
    "Paid": {
      "field": "items",
      "op": "gt",
      "rule": "count",
      "source": "context",
      "value": 0
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
          "field": "total",
          "op": "gt",
          "rule": "compare",
          "source": "context",
          "value": 0
        },
        {
          "field": "receipt",
          "rule": "absent",
          "source": "context"
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
      "trigger": "Restart"
    },
    {
      "from": "Review",
      "to": "Cart",
      "trigger": "Back"
    },
    {
      "effect": {
        "keyPrefix": "checkout:charge",
        "type": "Trax.Cli.Tests.Fakes.ICheckoutCharge"
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
            "field": "total",
            "op": "gt",
            "rule": "compare",
            "source": "context",
            "value": 0
          },
          {
            "field": "receipt",
            "rule": "present",
            "source": "input"
          }
        ]
      },
      "guardMessage": "A payable order needs items, a positive total, and a receipt.",
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
    "Back",
    "Next",
    "Pay",
    "Restart"
  ],
  "version": 1
} as IrDocument;

/** SHA-256 of this machine's IR — the version-skew handshake token (matches C#'s IMachine.SchemaHash). */
export const irHash = "7ed747818303694e14df969d1422a25f90e39dff6e9aa20532b84d409066a7c4";

/** The checkout machine, built from the IR and typed by CheckoutSpec. No hand-written twin. */
export const checkout = typedMachineFromIr<CheckoutSpec>(ir, irHash);
