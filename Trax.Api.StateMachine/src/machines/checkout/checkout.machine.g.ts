// AUTO-GENERATED from checkout.ir.json by generateMachineFactory. Do not edit by hand.

import { SnapshotMachine } from "../../machine";
import { machineFromIr, type IrDocument } from "../../rules/irMachine";
import { TypedMachine } from "../../typed";
import type { CheckoutSpec, CheckoutState, CheckoutTrigger } from "./checkout.contexts.g";

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

// The runtime machine's states/triggers are exactly those in the IR; the cast narrows the string
// generics to the generated unions so TypedMachine<CheckoutSpec> lines up.
const core = new SnapshotMachine(machineFromIr(ir)) as unknown as SnapshotMachine<
  CheckoutState,
  CheckoutTrigger
>;

/** The checkout machine, built from the IR and typed by CheckoutSpec. No hand-written twin. */
export const checkout = new TypedMachine<CheckoutSpec>(core);
