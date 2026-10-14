// AUTO-GENERATED from gauge.ir.json by generateMachineFactory. Do not edit by hand.

import { typedMachineFromIr } from "../../typed";
import type { IrDocument } from "../../rules/irMachine";
import type { GaugeSpec } from "./gauge.contexts.g";

const ir = {
  "committedStates": [],
  "context": {
    "Idle": {
      "fields": []
    },
    "Reading": {
      "fields": [
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "reading",
          "nullable": false,
          "type": "integer"
        }
      ]
    }
  },
  "id": "gauge",
  "initialContext": {},
  "initialState": "Idle",
  "inputs": {
    "Lower": {
      "fields": [
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "value",
          "nullable": false,
          "type": "integer"
        }
      ]
    },
    "Raise": {
      "fields": [
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "value",
          "nullable": false,
          "type": "integer"
        }
      ]
    }
  },
  "states": [
    "Idle",
    "Reading"
  ],
  "transitions": [
    {
      "from": "Idle",
      "guard": {
        "field": "value",
        "op": "lt",
        "rule": "compare",
        "source": "input",
        "value": 13
      },
      "guardMessage": "A lowered reading is below 13.",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "reading",
            "value": {
              "input": "value"
            }
          }
        ]
      },
      "to": "Reading",
      "trigger": "Lower"
    },
    {
      "from": "Idle",
      "guard": {
        "field": "value",
        "op": "gte",
        "rule": "compare",
        "source": "input",
        "value": 2
      },
      "guardMessage": "A raised reading is at least 2.",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "reading",
            "value": {
              "input": "value"
            }
          }
        ]
      },
      "to": "Reading",
      "trigger": "Raise"
    },
    {
      "from": "Reading",
      "guard": {
        "field": "reading",
        "op": "lte",
        "rule": "compare",
        "source": "context",
        "value": 10
      },
      "guardMessage": "Only a reading of at most 10 settles.",
      "reduce": {
        "reduce": "clear"
      },
      "to": "Idle",
      "trigger": "Settle"
    }
  ],
  "triggers": [
    "Lower",
    "Raise",
    "Settle"
  ],
  "version": 1
} as IrDocument;

/** SHA-256 of this machine's IR — the version-skew handshake token (matches C#'s IMachine.SchemaHash). */
export const irHash = "abe386c3e05d2c7afe6621cd47e879c71d1e916428f139e151bfe661010614a5";

/** The gauge machine, built from the IR and typed by GaugeSpec. No hand-written twin. */
export const gauge = typedMachineFromIr<GaugeSpec>(ir, irHash);
