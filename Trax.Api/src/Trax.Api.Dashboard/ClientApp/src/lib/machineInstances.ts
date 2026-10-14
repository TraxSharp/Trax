import type { MachineInstance, SnapshotOwnerKind } from "../types";

// The State machines pages' paths, as the Blazor dashboard's MachineInstanceRoutes builds them. An
// instance's path names its owner kind, so a user's draft and a system instance under the same id
// never share one, and a user's draft carries its row id, because several users can each hold a
// draft under one id.

export const MACHINE_INSTANCES_PATH = "/state-machines";

/** The path segment for an owner kind. */
export function ownerSegment(ownerKind: SnapshotOwnerKind): "system" | "user" {
  return ownerKind === "SYSTEM" ? "system" : "user";
}

/** The owner kind a path segment names, or null for any other text. */
export function parseOwnerSegment(segment: string | undefined): SnapshotOwnerKind | null {
  return segment === "system" ? "SYSTEM" : segment === "user" ? "USER" : null;
}

/** The page of one instance. */
export function machineInstancePath(
  instance: Pick<MachineInstance, "machine" | "ownerKind" | "id" | "rowId">,
): string {
  const path = `${MACHINE_INSTANCES_PATH}/${encodeURIComponent(instance.machine)}/${ownerSegment(instance.ownerKind)}/${instance.id}`;
  return instance.ownerKind === "USER" ? `${path}?row=${instance.rowId}` : path;
}

/** "System" or "User", as the list shows an owner kind. */
export function ownerLabel(ownerKind: SnapshotOwnerKind): string {
  return ownerKind === "SYSTEM" ? "System" : "User";
}
