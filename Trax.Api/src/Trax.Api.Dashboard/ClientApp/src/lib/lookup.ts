import type { LookupKind, LookupMatch } from "../types";
import { machineInstancePath } from "./machineInstances";

/** How each kind of match reads in the search's list. */
export const LOOKUP_KIND_LABEL: Record<LookupKind, string> = {
  RUN: "Run",
  MANIFEST: "Manifest",
  WORK_QUEUE_ENTRY: "Queued entry",
  DEAD_LETTER: "Dead letter",
  MACHINE_INSTANCE: "Machine instance",
  TRAIN: "Train",
};

/** The console page a match opens. */
export function lookupPath(m: LookupMatch): string {
  switch (m.kind) {
    case "RUN":
      return `/runs/${m.id}`;
    case "MANIFEST":
      return `/schedules/${m.id}`;
    case "WORK_QUEUE_ENTRY":
      return `/runs/queued/${m.id}`;
    case "DEAD_LETTER":
      return `/runs/dead-letters/${m.id}`;
    case "TRAIN":
      return `/trains/${encodeURIComponent(m.id)}`;
    case "MACHINE_INSTANCE":
      return machineInstancePath({
        machine: m.machine ?? "",
        ownerKind: m.ownerKind ?? "SYSTEM",
        id: m.id,
        rowId: m.rowId ?? 0,
      });
  }
}
