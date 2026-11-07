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
    }
  },
  "id": "shift",
  "initialContext": {},
  "initialState": "Off",
  "inputs": {},
  "states": [
    "Off",
    "On"
  ],
  "transitions": [
    {
      "from": "Off",
      "to": "On",
      "trigger": "ClockIn"
    }
  ],
  "triggers": [
    "ClockIn"
  ],
  "version": 1
} as IrDocument;

/** SHA-256 of this machine's IR — the version-skew handshake token (matches C#'s IMachine.SchemaHash). */
export const irHash = "d7e52fc1fc8a6b097da75e5b336077cd81e382baa13e81b5792906e35b7e51ea";

/** The shift machine, built from the IR and typed by ShiftSpec. No hand-written twin. */
export const shift = typedMachineFromIr<ShiftSpec>(ir, irHash);
