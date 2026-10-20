// AUTO-GENERATED from reminder.ir.json by generateMachineFactory. Do not edit by hand.

import { typedMachineFromIr } from "../../typed";
import type { IrDocument } from "../../rules/irMachine";
import type { ReminderSpec } from "./reminder.contexts.g";

const ir = {
  "committedStates": [],
  "context": {
    "Done": {
      "fields": []
    },
    "Expired": {
      "fields": []
    },
    "Idle": {
      "fields": []
    },
    "Review": {
      "fields": []
    }
  },
  "id": "reminder",
  "initialContext": {},
  "initialState": "Idle",
  "inputs": {},
  "states": [
    "Done",
    "Expired",
    "Idle",
    "Review"
  ],
  "timers": [
    {
      "after": "PT10M",
      "rules": [
        "AnyAuthenticated",
        "Requires(Editor)"
      ],
      "state": "Review",
      "target": "Expired",
      "trigger": "Review.after"
    }
  ],
  "transitions": [
    {
      "from": "Idle",
      "to": "Review",
      "trigger": "Open"
    },
    {
      "from": "Review",
      "to": "Done",
      "trigger": "Approve"
    },
    {
      "from": "Review",
      "to": "Review",
      "trigger": "Extend"
    }
  ],
  "triggers": [
    "Approve",
    "Extend",
    "Open"
  ],
  "version": 1
} as IrDocument;

/** SHA-256 of this machine's IR — the version-skew handshake token (matches C#'s IMachine.SchemaHash). */
export const irHash = "95db643cae0a0ada332c6f27e9b53de5a3c1855229383d0eb8066304f7f9c342";

/** The reminder machine, built from the IR and typed by ReminderSpec. No hand-written twin. */
export const reminder = typedMachineFromIr<ReminderSpec>(ir, irHash);
