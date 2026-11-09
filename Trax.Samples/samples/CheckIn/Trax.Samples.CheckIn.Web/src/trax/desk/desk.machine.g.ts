// AUTO-GENERATED from desk.ir.json by generateMachineFactory. Do not edit by hand.
// Imports the engine from "@traxsharp/state-machine" (trax machine generate --import-style specifier, the
// default without --engine-src). Name another module with --specifier <module>, or import an engine
// checkout by relative path with --import-style relative --engine-src <engine/src>.

import { typedMachineFromIr, type IrDocument } from "@traxsharp/state-machine";
import type { DeskSpec } from "./desk.contexts.g";

const ir = {
  "irFormat": 1,
  "committedStates": [],
  "context": {
    "Closing": {
      "fields": []
    },
    "Done": {
      "fields": []
    },
    "Open": {
      "fields": []
    },
    "Waiting": {
      "fields": []
    }
  },
  "id": "desk",
  "initialContext": {},
  "initialState": "Waiting",
  "inputs": {},
  "states": [
    "Closing",
    "Done",
    "Open",
    "Waiting"
  ],
  "timers": [
    {
      "after": "PT2S",
      "rules": [
        "Requires(agent)"
      ],
      "state": "Closing",
      "target": "Done",
      "trigger": "Closing.after"
    }
  ],
  "transitions": [
    {
      "from": "Open",
      "to": "Closing",
      "trigger": "CloseGate"
    },
    {
      "from": "Waiting",
      "to": "Open",
      "trigger": "OpenDesk"
    }
  ],
  "triggers": [
    "CloseGate",
    "OpenDesk"
  ],
  "version": 1
} as IrDocument;

/** SHA-256 of this machine's IR — the version-skew handshake token (matches C#'s IMachine.SchemaHash). */
export const irHash = "9464b4b999c7a330fbc072f882ffd3d067a74bc0ad278ff4c41a25a3ba63c55e";

/** The desk machine, built from the IR and typed by DeskSpec. No hand-written twin. */
export const desk = typedMachineFromIr<DeskSpec>(ir, irHash);
