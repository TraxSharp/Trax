// AUTO-GENERATED from shift.ir.json by generateMachineFactory. Do not edit by hand.
// Imports the engine from "@traxsharp/state-machine" (trax machine generate --import-style specifier, the
// default without --engine-src). Name another module with --specifier <module>, or import an engine
// checkout by relative path with --import-style relative --engine-src <engine/src>.

import { typedMachineFromIr, type IrDocument } from "@traxsharp/state-machine";
import type { ShiftSpec } from "./shift.contexts.g";

const ir = {
  "irFormat": 1,
  "committedStates": [],
  "context": {
    "Off": {
      "fields": []
    },
    "On": {
      "fields": []
    },
    "Oven": {
      "fields": []
    },
    "Over": {
      "fields": []
    }
  },
  "id": "shift",
  "initialContext": {},
  "initialState": "Off",
  "inputs": {},
  "states": [
    "Off",
    "On",
    "Oven",
    "Over"
  ],
  "timers": [
    {
      "after": "PT2S",
      "rules": [
        "Requires(baker)"
      ],
      "state": "Oven",
      "target": "On",
      "trigger": "Oven.after"
    }
  ],
  "transitions": [
    {
      "from": "Off",
      "to": "On",
      "trigger": "ClockIn"
    },
    {
      "from": "On",
      "to": "Over",
      "trigger": "ClockOut"
    },
    {
      "from": "On",
      "to": "Oven",
      "trigger": "FireOven"
    }
  ],
  "triggers": [
    "ClockIn",
    "ClockOut",
    "FireOven"
  ],
  "version": 1
} as IrDocument;

/** SHA-256 of this machine's IR — the version-skew handshake token (matches C#'s IMachine.SchemaHash). */
export const irHash = "f77c9cfa7ce86f89baf4943bfa949913cabaa2bf613b5f6e7bc86d92c42afc00";

/** The shift machine, built from the IR and typed by ShiftSpec. No hand-written twin. */
export const shift = typedMachineFromIr<ShiftSpec>(ir, irHash);
