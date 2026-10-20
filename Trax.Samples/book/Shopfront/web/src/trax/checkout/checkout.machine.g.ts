// AUTO-GENERATED from checkout.ir.json by generateMachineFactory. Do not edit by hand.
// Imports the engine from "@traxsharp/state-machine" (trax machine generate --import-style specifier, the
// default without --engine-src). Name another module with --specifier <module>, or import an engine
// checkout by relative path with --import-style relative --engine-src <engine/src>.

import { typedMachineFromIr, type IrDocument } from "@traxsharp/state-machine";
import type { CheckoutSpec } from "./checkout.contexts.g";

const ir = {
  "irFormat": 1,
  "committedStates": [],
  "context": {
    "Abandoned": {
      "fields": [
        {
          "constraints": [],
          "name": "address",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "lines",
          "nullable": true,
          "type": "array"
        },
        {
          "constraints": [],
          "name": "paymentToken",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "Address": {
      "fields": [
        {
          "constraints": [],
          "name": "address",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "lines",
          "nullable": true,
          "type": "array"
        },
        {
          "constraints": [],
          "name": "paymentToken",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "Cart": {
      "fields": [
        {
          "constraints": [],
          "name": "address",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "lines",
          "nullable": true,
          "type": "array"
        },
        {
          "constraints": [],
          "name": "paymentToken",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "Delivered": {
      "fields": [
        {
          "constraints": [],
          "name": "address",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "lines",
          "nullable": true,
          "type": "array"
        },
        {
          "constraints": [],
          "format": "uuid",
          "name": "orderId",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "format": "uuid",
          "name": "parcelId",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "paymentToken",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "Failed": {
      "fields": [
        {
          "constraints": [],
          "name": "address",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "lines",
          "nullable": true,
          "type": "array"
        },
        {
          "constraints": [],
          "name": "paymentToken",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "Payment": {
      "fields": [
        {
          "constraints": [],
          "name": "address",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "lines",
          "nullable": true,
          "type": "array"
        },
        {
          "constraints": [],
          "name": "paymentToken",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "Placed": {
      "fields": [
        {
          "constraints": [],
          "name": "address",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "lines",
          "nullable": true,
          "type": "array"
        },
        {
          "constraints": [],
          "format": "uuid",
          "name": "orderId",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "paymentToken",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "Placing": {
      "fields": [
        {
          "constraints": [],
          "name": "address",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "lines",
          "nullable": true,
          "type": "array"
        },
        {
          "constraints": [],
          "name": "paymentToken",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "Shipping": {
      "fields": [
        {
          "constraints": [],
          "name": "address",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "lines",
          "nullable": true,
          "type": "array"
        },
        {
          "constraints": [],
          "format": "uuid",
          "name": "orderId",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "format": "uuid",
          "name": "parcelId",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "paymentToken",
          "nullable": true,
          "type": "string"
        }
      ]
    }
  },
  "id": "checkout",
  "initialContext": {},
  "initialState": "Cart",
  "inputs": {
    "ChoosePayment": {
      "fields": [
        {
          "constraints": [],
          "name": "paymentToken",
          "nullable": false,
          "type": "string"
        }
      ]
    },
    "SetAddress": {
      "fields": [
        {
          "constraints": [],
          "name": "address",
          "nullable": false,
          "type": "string"
        }
      ]
    },
    "SetLines": {
      "fields": [
        {
          "constraints": [],
          "fields": [
            {
              "constraints": [],
              "maximum": 2147483647,
              "minimum": -2147483648,
              "name": "quantity",
              "nullable": false,
              "type": "integer"
            },
            {
              "constraints": [],
              "name": "sku",
              "nullable": false,
              "type": "string"
            }
          ],
          "name": "lines",
          "nullable": false,
          "type": "array"
        }
      ]
    }
  },
  "outcomes": {
    "Placing.cancelled": {
      "edges": [
        {
          "to": "Failed"
        }
      ],
      "outcome": "cancelled",
      "state": "Placing"
    },
    "Placing.done": {
      "edges": [
        {
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "orderId",
                "value": {
                  "input": "orderId"
                }
              }
            ]
          },
          "to": "Placed"
        }
      ],
      "outcome": "done",
      "output": {
        "fields": [
          {
            "constraints": [],
            "format": "uuid",
            "name": "orderId",
            "nullable": false,
            "type": "string"
          },
          {
            "constraints": [],
            "maximum": 7.922816251426433e+28,
            "minimum": -7.922816251426433e+28,
            "name": "total",
            "nullable": false,
            "type": "number"
          },
          {
            "constraints": [],
            "name": "transactionId",
            "nullable": false,
            "type": "string"
          }
        ]
      },
      "state": "Placing"
    },
    "Placing.failed": {
      "edges": [
        {
          "to": "Failed"
        }
      ],
      "outcome": "failed",
      "state": "Placing"
    },
    "Shipping.cancelled": {
      "edges": [
        {
          "to": "Placed"
        }
      ],
      "outcome": "cancelled",
      "state": "Shipping"
    },
    "Shipping.done": {
      "edges": [
        {
          "to": "Delivered"
        }
      ],
      "outcome": "done",
      "output": {
        "fields": []
      },
      "state": "Shipping"
    },
    "Shipping.failed": {
      "edges": [
        {
          "to": "Placed"
        }
      ],
      "outcome": "failed",
      "state": "Shipping"
    }
  },
  "states": [
    "Abandoned",
    "Address",
    "Cart",
    "Delivered",
    "Failed",
    "Payment",
    "Placed",
    "Placing",
    "Shipping"
  ],
  "timers": [
    {
      "after": "PT2H",
      "rules": [
        "AnyAuthenticated",
        "Requires(Customer)"
      ],
      "state": "Cart",
      "target": "Abandoned",
      "trigger": "Cart.after"
    }
  ],
  "transitions": [
    {
      "from": "Abandoned",
      "reduce": {
        "reduce": "reset"
      },
      "to": "Cart",
      "trigger": "StartOver"
    },
    {
      "from": "Address",
      "to": "Cart",
      "trigger": "Back"
    },
    {
      "from": "Address",
      "guard": {
        "field": "paymentToken",
        "rule": "nonEmpty",
        "source": "input"
      },
      "guardMessage": "Choose how to pay.",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "paymentToken",
            "value": {
              "input": "paymentToken"
            }
          }
        ]
      },
      "to": "Payment",
      "trigger": "ChoosePayment"
    },
    {
      "from": "Cart",
      "guard": {
        "rule": "all",
        "rules": [
          {
            "field": "lines",
            "op": "gte",
            "rule": "count",
            "source": "context",
            "value": 1
          },
          {
            "field": "address",
            "rule": "nonEmpty",
            "source": "input"
          }
        ]
      },
      "guardMessage": "Put something in the cart and give an address to send it to.",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "address",
            "value": {
              "input": "address"
            }
          }
        ]
      },
      "to": "Address",
      "trigger": "SetAddress"
    },
    {
      "from": "Cart",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "lines",
            "value": {
              "input": "lines"
            }
          }
        ]
      },
      "to": "Cart",
      "trigger": "SetLines"
    },
    {
      "from": "Failed",
      "to": "Placing",
      "trigger": "Retry"
    },
    {
      "from": "Payment",
      "to": "Address",
      "trigger": "Back"
    },
    {
      "from": "Payment",
      "to": "Placing",
      "trigger": "PlaceOrder"
    },
    {
      "from": "Placed",
      "to": "Shipping",
      "trigger": "Track"
    }
  ],
  "triggers": [
    "Back",
    "ChoosePayment",
    "PlaceOrder",
    "Retry",
    "SetAddress",
    "SetLines",
    "StartOver",
    "Track"
  ],
  "version": 1
} as IrDocument;

/** SHA-256 of this machine's IR — the version-skew handshake token (matches C#'s IMachine.SchemaHash). */
export const irHash = "75d91032a56494089fc55eec5c11a35f22253ed8a31c543d22310520668d4258";

/** The checkout machine, built from the IR and typed by CheckoutSpec. No hand-written twin. */
export const checkout = typedMachineFromIr<CheckoutSpec>(ir, irHash);
