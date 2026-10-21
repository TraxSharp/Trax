// AUTO-GENERATED from assignment.ir.json by generateMachineFactory. Do not edit by hand.
// Imports the engine from "@traxsharp/state-machine" (trax machine generate --import-style specifier, the
// default without --engine-src). Name another module with --specifier <module>, or import an engine
// checkout by relative path with --import-style relative --engine-src <engine/src>.

import { typedMachineFromIr, type IrDocument } from "@traxsharp/state-machine";
import type { AssignmentSpec } from "./assignment.contexts.g";

const sharedOutput0 = {
  "fields": [
    {
      "constraints": [],
      "name": "accepted",
      "nullable": false,
      "type": "boolean"
    },
    {
      "constraints": [],
      "maximum": 2147483647,
      "minimum": -2147483648,
      "name": "failedCases",
      "nullable": true,
      "type": "integer"
    },
    {
      "constraints": [],
      "name": "message",
      "nullable": false,
      "type": "string"
    }
  ]
};

const ir = {
  "irFormat": 1,
  "committedStates": [],
  "context": {
    "Accepted": {
      "fields": [
        {
          "constraints": [],
          "name": "area",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "decision",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "expectedShipping",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "failedCases",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "freeFrom",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "inclusive",
          "nullable": true,
          "type": "boolean"
        },
        {
          "constraints": [],
          "name": "reason",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "subtotal",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [
            {
              "field": "subtotals",
              "rule": "arrayOf",
              "source": "context",
              "type": "number"
            }
          ],
          "name": "subtotals",
          "nullable": true,
          "type": "array",
          "values": {
            "maximum": 7.922816251426433e+28,
            "minimum": -7.922816251426433e+28,
            "nullable": false,
            "type": "number"
          }
        },
        {
          "constraints": [
            {
              "field": "ticketId",
              "rule": "nonEmpty",
              "source": "context"
            }
          ],
          "name": "ticketId",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "verdict",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "CheckingClose": {
      "fields": [
        {
          "constraints": [],
          "name": "area",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "decision",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "expectedShipping",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "failedCases",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "freeFrom",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "inclusive",
          "nullable": true,
          "type": "boolean"
        },
        {
          "constraints": [],
          "name": "reason",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "subtotal",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [
            {
              "field": "subtotals",
              "rule": "arrayOf",
              "source": "context",
              "type": "number"
            }
          ],
          "name": "subtotals",
          "nullable": true,
          "type": "array",
          "values": {
            "maximum": 7.922816251426433e+28,
            "minimum": -7.922816251426433e+28,
            "nullable": false,
            "type": "number"
          }
        },
        {
          "constraints": [
            {
              "field": "ticketId",
              "rule": "nonEmpty",
              "source": "context"
            }
          ],
          "name": "ticketId",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "verdict",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "CheckingFix": {
      "fields": [
        {
          "constraints": [],
          "name": "area",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "decision",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "expectedShipping",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "failedCases",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "freeFrom",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "inclusive",
          "nullable": true,
          "type": "boolean"
        },
        {
          "constraints": [],
          "name": "reason",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "subtotal",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [
            {
              "field": "subtotals",
              "rule": "arrayOf",
              "source": "context",
              "type": "number"
            }
          ],
          "name": "subtotals",
          "nullable": true,
          "type": "array",
          "values": {
            "maximum": 7.922816251426433e+28,
            "minimum": -7.922816251426433e+28,
            "nullable": false,
            "type": "number"
          }
        },
        {
          "constraints": [
            {
              "field": "ticketId",
              "rule": "nonEmpty",
              "source": "context"
            }
          ],
          "name": "ticketId",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "verdict",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "CheckingQa": {
      "fields": [
        {
          "constraints": [],
          "name": "area",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "decision",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "expectedShipping",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "failedCases",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "freeFrom",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "inclusive",
          "nullable": true,
          "type": "boolean"
        },
        {
          "constraints": [],
          "name": "reason",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "subtotal",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [
            {
              "field": "subtotals",
              "rule": "arrayOf",
              "source": "context",
              "type": "number"
            }
          ],
          "name": "subtotals",
          "nullable": true,
          "type": "array",
          "values": {
            "maximum": 7.922816251426433e+28,
            "minimum": -7.922816251426433e+28,
            "nullable": false,
            "type": "number"
          }
        },
        {
          "constraints": [
            {
              "field": "ticketId",
              "rule": "nonEmpty",
              "source": "context"
            }
          ],
          "name": "ticketId",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "verdict",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "CheckingRepro": {
      "fields": [
        {
          "constraints": [],
          "name": "area",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "decision",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "expectedShipping",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "failedCases",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "freeFrom",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "inclusive",
          "nullable": true,
          "type": "boolean"
        },
        {
          "constraints": [],
          "name": "reason",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "subtotal",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [
            {
              "field": "subtotals",
              "rule": "arrayOf",
              "source": "context",
              "type": "number"
            }
          ],
          "name": "subtotals",
          "nullable": true,
          "type": "array",
          "values": {
            "maximum": 7.922816251426433e+28,
            "minimum": -7.922816251426433e+28,
            "nullable": false,
            "type": "number"
          }
        },
        {
          "constraints": [
            {
              "field": "ticketId",
              "rule": "nonEmpty",
              "source": "context"
            }
          ],
          "name": "ticketId",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "verdict",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "CheckingReview": {
      "fields": [
        {
          "constraints": [],
          "name": "area",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "decision",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "expectedShipping",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "failedCases",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "freeFrom",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "inclusive",
          "nullable": true,
          "type": "boolean"
        },
        {
          "constraints": [],
          "name": "reason",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "subtotal",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [
            {
              "field": "subtotals",
              "rule": "arrayOf",
              "source": "context",
              "type": "number"
            }
          ],
          "name": "subtotals",
          "nullable": true,
          "type": "array",
          "values": {
            "maximum": 7.922816251426433e+28,
            "minimum": -7.922816251426433e+28,
            "nullable": false,
            "type": "number"
          }
        },
        {
          "constraints": [
            {
              "field": "ticketId",
              "rule": "nonEmpty",
              "source": "context"
            }
          ],
          "name": "ticketId",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "verdict",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "CheckingTriage": {
      "fields": [
        {
          "constraints": [],
          "name": "area",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "decision",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "expectedShipping",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "failedCases",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "freeFrom",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "inclusive",
          "nullable": true,
          "type": "boolean"
        },
        {
          "constraints": [],
          "name": "reason",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "subtotal",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [
            {
              "field": "subtotals",
              "rule": "arrayOf",
              "source": "context",
              "type": "number"
            }
          ],
          "name": "subtotals",
          "nullable": true,
          "type": "array",
          "values": {
            "maximum": 7.922816251426433e+28,
            "minimum": -7.922816251426433e+28,
            "nullable": false,
            "type": "number"
          }
        },
        {
          "constraints": [
            {
              "field": "ticketId",
              "rule": "nonEmpty",
              "source": "context"
            }
          ],
          "name": "ticketId",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "verdict",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "Closed": {
      "fields": [
        {
          "constraints": [],
          "name": "area",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "decision",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "expectedShipping",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "failedCases",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "freeFrom",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "inclusive",
          "nullable": true,
          "type": "boolean"
        },
        {
          "constraints": [],
          "name": "reason",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "subtotal",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [
            {
              "field": "subtotals",
              "rule": "arrayOf",
              "source": "context",
              "type": "number"
            }
          ],
          "name": "subtotals",
          "nullable": true,
          "type": "array",
          "values": {
            "maximum": 7.922816251426433e+28,
            "minimum": -7.922816251426433e+28,
            "nullable": false,
            "type": "number"
          }
        },
        {
          "constraints": [
            {
              "field": "ticketId",
              "rule": "nonEmpty",
              "source": "context"
            }
          ],
          "name": "ticketId",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "verdict",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "Rejected": {
      "fields": [
        {
          "constraints": [],
          "name": "area",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "decision",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "expectedShipping",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "failedCases",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "freeFrom",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "inclusive",
          "nullable": true,
          "type": "boolean"
        },
        {
          "constraints": [],
          "name": "reason",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "subtotal",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [
            {
              "field": "subtotals",
              "rule": "arrayOf",
              "source": "context",
              "type": "number"
            }
          ],
          "name": "subtotals",
          "nullable": true,
          "type": "array",
          "values": {
            "maximum": 7.922816251426433e+28,
            "minimum": -7.922816251426433e+28,
            "nullable": false,
            "type": "number"
          }
        },
        {
          "constraints": [
            {
              "field": "ticketId",
              "rule": "nonEmpty",
              "source": "context"
            }
          ],
          "name": "ticketId",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "verdict",
          "nullable": true,
          "type": "string"
        }
      ]
    },
    "Working": {
      "fields": [
        {
          "constraints": [],
          "name": "area",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "decision",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "expectedShipping",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "failedCases",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "freeFrom",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "inclusive",
          "nullable": true,
          "type": "boolean"
        },
        {
          "constraints": [],
          "name": "reason",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "subtotal",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [
            {
              "field": "subtotals",
              "rule": "arrayOf",
              "source": "context",
              "type": "number"
            }
          ],
          "name": "subtotals",
          "nullable": true,
          "type": "array",
          "values": {
            "maximum": 7.922816251426433e+28,
            "minimum": -7.922816251426433e+28,
            "nullable": false,
            "type": "number"
          }
        },
        {
          "constraints": [
            {
              "field": "ticketId",
              "rule": "nonEmpty",
              "source": "context"
            }
          ],
          "name": "ticketId",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "verdict",
          "nullable": true,
          "type": "string"
        }
      ]
    }
  },
  "id": "assignment",
  "initialContext": {
    "ticketId": "unknown"
  },
  "initialState": "Working",
  "inputs": {
    "SendBack": {
      "fields": [
        {
          "constraints": [
            {
              "field": "reason",
              "rule": "nonEmpty",
              "source": "input"
            }
          ],
          "name": "reason",
          "nullable": false,
          "type": "string"
        }
      ]
    },
    "SubmitChecks": {
      "fields": [
        {
          "constraints": [
            {
              "field": "subtotals",
              "rule": "arrayOf",
              "source": "input",
              "type": "number"
            }
          ],
          "name": "subtotals",
          "nullable": false,
          "type": "array",
          "values": {
            "maximum": 7.922816251426433e+28,
            "minimum": -7.922816251426433e+28,
            "nullable": false,
            "type": "number"
          }
        }
      ]
    },
    "SubmitFix": {
      "fields": [
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "freeFrom",
          "nullable": false,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "inclusive",
          "nullable": false,
          "type": "boolean"
        }
      ]
    },
    "SubmitRepro": {
      "fields": [
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "expectedShipping",
          "nullable": false,
          "type": "number"
        },
        {
          "constraints": [],
          "maximum": 7.922816251426433e+28,
          "minimum": -7.922816251426433e+28,
          "name": "subtotal",
          "nullable": false,
          "type": "number"
        }
      ]
    },
    "Triage": {
      "fields": [
        {
          "constraints": [
            {
              "field": "area",
              "rule": "nonEmpty",
              "source": "input"
            }
          ],
          "name": "area",
          "nullable": false,
          "type": "string"
        }
      ]
    }
  },
  "outcomes": {
    "CheckingClose.cancelled": {
      "edges": [
        {
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "const": "the check did not finish; send again"
                }
              }
            ]
          },
          "to": "Rejected"
        }
      ],
      "outcome": "cancelled",
      "state": "CheckingClose"
    },
    "CheckingClose.done": {
      "edges": [
        {
          "guard": {
            "field": "accepted",
            "rule": "boolEquals",
            "source": "input",
            "value": true
          },
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "input": "message"
                }
              },
              {
                "field": "failedCases",
                "value": {
                  "input": "failedCases"
                }
              }
            ]
          },
          "to": "Closed"
        },
        {
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "input": "message"
                }
              },
              {
                "field": "failedCases",
                "value": {
                  "input": "failedCases"
                }
              }
            ]
          },
          "to": "Rejected"
        }
      ],
      "outcome": "done",
      "output": sharedOutput0,
      "state": "CheckingClose"
    },
    "CheckingClose.failed": {
      "edges": [
        {
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "const": "the check did not finish; send again"
                }
              }
            ]
          },
          "to": "Rejected"
        }
      ],
      "outcome": "failed",
      "state": "CheckingClose"
    },
    "CheckingFix.cancelled": {
      "edges": [
        {
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "const": "the check did not finish; send again"
                }
              }
            ]
          },
          "to": "Rejected"
        }
      ],
      "outcome": "cancelled",
      "state": "CheckingFix"
    },
    "CheckingFix.done": {
      "edges": [
        {
          "guard": {
            "field": "accepted",
            "rule": "boolEquals",
            "source": "input",
            "value": true
          },
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "input": "message"
                }
              },
              {
                "field": "failedCases",
                "value": {
                  "input": "failedCases"
                }
              }
            ]
          },
          "to": "Accepted"
        },
        {
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "input": "message"
                }
              },
              {
                "field": "failedCases",
                "value": {
                  "input": "failedCases"
                }
              }
            ]
          },
          "to": "Rejected"
        }
      ],
      "outcome": "done",
      "output": sharedOutput0,
      "state": "CheckingFix"
    },
    "CheckingFix.failed": {
      "edges": [
        {
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "const": "the check did not finish; send again"
                }
              }
            ]
          },
          "to": "Rejected"
        }
      ],
      "outcome": "failed",
      "state": "CheckingFix"
    },
    "CheckingQa.cancelled": {
      "edges": [
        {
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "const": "the check did not finish; send again"
                }
              }
            ]
          },
          "to": "Rejected"
        }
      ],
      "outcome": "cancelled",
      "state": "CheckingQa"
    },
    "CheckingQa.done": {
      "edges": [
        {
          "guard": {
            "field": "accepted",
            "rule": "boolEquals",
            "source": "input",
            "value": true
          },
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "input": "message"
                }
              },
              {
                "field": "failedCases",
                "value": {
                  "input": "failedCases"
                }
              }
            ]
          },
          "to": "Accepted"
        },
        {
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "input": "message"
                }
              },
              {
                "field": "failedCases",
                "value": {
                  "input": "failedCases"
                }
              }
            ]
          },
          "to": "Rejected"
        }
      ],
      "outcome": "done",
      "output": sharedOutput0,
      "state": "CheckingQa"
    },
    "CheckingQa.failed": {
      "edges": [
        {
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "const": "the check did not finish; send again"
                }
              }
            ]
          },
          "to": "Rejected"
        }
      ],
      "outcome": "failed",
      "state": "CheckingQa"
    },
    "CheckingRepro.cancelled": {
      "edges": [
        {
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "const": "the check did not finish; send again"
                }
              }
            ]
          },
          "to": "Rejected"
        }
      ],
      "outcome": "cancelled",
      "state": "CheckingRepro"
    },
    "CheckingRepro.done": {
      "edges": [
        {
          "guard": {
            "field": "accepted",
            "rule": "boolEquals",
            "source": "input",
            "value": true
          },
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "input": "message"
                }
              },
              {
                "field": "failedCases",
                "value": {
                  "input": "failedCases"
                }
              }
            ]
          },
          "to": "Accepted"
        },
        {
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "input": "message"
                }
              },
              {
                "field": "failedCases",
                "value": {
                  "input": "failedCases"
                }
              }
            ]
          },
          "to": "Rejected"
        }
      ],
      "outcome": "done",
      "output": sharedOutput0,
      "state": "CheckingRepro"
    },
    "CheckingRepro.failed": {
      "edges": [
        {
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "const": "the check did not finish; send again"
                }
              }
            ]
          },
          "to": "Rejected"
        }
      ],
      "outcome": "failed",
      "state": "CheckingRepro"
    },
    "CheckingReview.cancelled": {
      "edges": [
        {
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "const": "the check did not finish; send again"
                }
              }
            ]
          },
          "to": "Rejected"
        }
      ],
      "outcome": "cancelled",
      "state": "CheckingReview"
    },
    "CheckingReview.done": {
      "edges": [
        {
          "guard": {
            "field": "accepted",
            "rule": "boolEquals",
            "source": "input",
            "value": true
          },
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "input": "message"
                }
              },
              {
                "field": "failedCases",
                "value": {
                  "input": "failedCases"
                }
              }
            ]
          },
          "to": "Accepted"
        },
        {
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "input": "message"
                }
              },
              {
                "field": "failedCases",
                "value": {
                  "input": "failedCases"
                }
              }
            ]
          },
          "to": "Rejected"
        }
      ],
      "outcome": "done",
      "output": sharedOutput0,
      "state": "CheckingReview"
    },
    "CheckingReview.failed": {
      "edges": [
        {
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "const": "the check did not finish; send again"
                }
              }
            ]
          },
          "to": "Rejected"
        }
      ],
      "outcome": "failed",
      "state": "CheckingReview"
    },
    "CheckingTriage.cancelled": {
      "edges": [
        {
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "const": "the check did not finish; send again"
                }
              }
            ]
          },
          "to": "Rejected"
        }
      ],
      "outcome": "cancelled",
      "state": "CheckingTriage"
    },
    "CheckingTriage.done": {
      "edges": [
        {
          "guard": {
            "field": "accepted",
            "rule": "boolEquals",
            "source": "input",
            "value": true
          },
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "input": "message"
                }
              },
              {
                "field": "failedCases",
                "value": {
                  "input": "failedCases"
                }
              }
            ]
          },
          "to": "Accepted"
        },
        {
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "input": "message"
                }
              },
              {
                "field": "failedCases",
                "value": {
                  "input": "failedCases"
                }
              }
            ]
          },
          "to": "Rejected"
        }
      ],
      "outcome": "done",
      "output": sharedOutput0,
      "state": "CheckingTriage"
    },
    "CheckingTriage.failed": {
      "edges": [
        {
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "verdict",
                "value": {
                  "const": "the check did not finish; send again"
                }
              }
            ]
          },
          "to": "Rejected"
        }
      ],
      "outcome": "failed",
      "state": "CheckingTriage"
    }
  },
  "states": [
    "Accepted",
    "CheckingClose",
    "CheckingFix",
    "CheckingQa",
    "CheckingRepro",
    "CheckingReview",
    "CheckingTriage",
    "Closed",
    "Rejected",
    "Working"
  ],
  "transitions": [
    {
      "from": "Rejected",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "decision",
            "value": {
              "const": "approve"
            }
          }
        ]
      },
      "to": "CheckingReview",
      "trigger": "Approve"
    },
    {
      "from": "Rejected",
      "guard": {
        "field": "failedCases",
        "op": "gte",
        "rule": "compare",
        "source": "context",
        "value": 2
      },
      "guardMessage": "Close needs at least two cases the server ran that did not reproduce it.",
      "to": "CheckingClose",
      "trigger": "Close"
    },
    {
      "from": "Rejected",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "decision",
            "value": {
              "const": "send-back"
            }
          },
          {
            "field": "reason",
            "value": {
              "input": "reason"
            }
          }
        ]
      },
      "to": "CheckingReview",
      "trigger": "SendBack"
    },
    {
      "from": "Rejected",
      "guard": {
        "field": "subtotals",
        "op": "gte",
        "rule": "count",
        "source": "input",
        "value": 1
      },
      "guardMessage": "Send at least one subtotal to check.",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "subtotals",
            "value": {
              "input": "subtotals"
            }
          }
        ]
      },
      "to": "CheckingQa",
      "trigger": "SubmitChecks"
    },
    {
      "from": "Rejected",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "freeFrom",
            "value": {
              "input": "freeFrom"
            }
          },
          {
            "field": "inclusive",
            "value": {
              "input": "inclusive"
            }
          }
        ]
      },
      "to": "CheckingFix",
      "trigger": "SubmitFix"
    },
    {
      "from": "Rejected",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "subtotal",
            "value": {
              "input": "subtotal"
            }
          },
          {
            "field": "expectedShipping",
            "value": {
              "input": "expectedShipping"
            }
          }
        ]
      },
      "to": "CheckingRepro",
      "trigger": "SubmitRepro"
    },
    {
      "from": "Rejected",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "area",
            "value": {
              "input": "area"
            }
          }
        ]
      },
      "to": "CheckingTriage",
      "trigger": "Triage"
    },
    {
      "from": "Working",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "decision",
            "value": {
              "const": "approve"
            }
          }
        ]
      },
      "to": "CheckingReview",
      "trigger": "Approve"
    },
    {
      "from": "Working",
      "guard": {
        "field": "failedCases",
        "op": "gte",
        "rule": "compare",
        "source": "context",
        "value": 2
      },
      "guardMessage": "Close needs at least two cases the server ran that did not reproduce it.",
      "to": "CheckingClose",
      "trigger": "Close"
    },
    {
      "from": "Working",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "decision",
            "value": {
              "const": "send-back"
            }
          },
          {
            "field": "reason",
            "value": {
              "input": "reason"
            }
          }
        ]
      },
      "to": "CheckingReview",
      "trigger": "SendBack"
    },
    {
      "from": "Working",
      "guard": {
        "field": "subtotals",
        "op": "gte",
        "rule": "count",
        "source": "input",
        "value": 1
      },
      "guardMessage": "Send at least one subtotal to check.",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "subtotals",
            "value": {
              "input": "subtotals"
            }
          }
        ]
      },
      "to": "CheckingQa",
      "trigger": "SubmitChecks"
    },
    {
      "from": "Working",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "freeFrom",
            "value": {
              "input": "freeFrom"
            }
          },
          {
            "field": "inclusive",
            "value": {
              "input": "inclusive"
            }
          }
        ]
      },
      "to": "CheckingFix",
      "trigger": "SubmitFix"
    },
    {
      "from": "Working",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "subtotal",
            "value": {
              "input": "subtotal"
            }
          },
          {
            "field": "expectedShipping",
            "value": {
              "input": "expectedShipping"
            }
          }
        ]
      },
      "to": "CheckingRepro",
      "trigger": "SubmitRepro"
    },
    {
      "from": "Working",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "area",
            "value": {
              "input": "area"
            }
          }
        ]
      },
      "to": "CheckingTriage",
      "trigger": "Triage"
    }
  ],
  "triggers": [
    "Approve",
    "Close",
    "SendBack",
    "SubmitChecks",
    "SubmitFix",
    "SubmitRepro",
    "Triage"
  ],
  "version": 1
} as IrDocument;

/** SHA-256 of this machine's IR — the version-skew handshake token (matches C#'s IMachine.SchemaHash). */
export const irHash = "f3e3aed3f55a059e9e41e9620980df07672379f668b53d72986c46cb18d8a9c1";

/** The assignment machine, built from the IR and typed by AssignmentSpec. No hand-written twin. */
export const assignment = typedMachineFromIr<AssignmentSpec>(ir, irHash);
