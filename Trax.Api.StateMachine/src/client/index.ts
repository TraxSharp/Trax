/**
 * `@trax/state-machine/client` — talking to a Trax server about a stored draft.
 *
 * The core package is pure: a reducer, a rule interpreter, and codegen, with no I/O anywhere. This
 * subpath is the one place that knows a server exists. It is a separate entry point so importing the
 * engine never drags a transport (or the `fetch` global) into a bundle that only wanted the reducer.
 *
 *   transport.ts    the four generic mutations, machine-agnostic — `(machine, id)` on every call
 *   draftSession.ts one machine + one draft id, bound; every method total (never throws)
 *
 * Typical wiring:
 *
 *   const client  = createSnapshotClient(createHttpExecutor('/graphql'));
 *   const session = createDraftSession({ client, machine: writeToCongress, id: DRAFT_ID });
 *   await session.save(machine.serialize(snapshot));
 */
export {
  createSnapshotClient,
  createHttpExecutor,
  type AdvanceOptions,
  type HttpExecutorOptions,
  type SnapshotClient,
  type SnapshotExecutor,
  type SnapshotOperation,
  type SnapshotOutput,
  type SnapshotProblem,
} from './transport';

export {
  createDraftSession,
  TRANSPORT_ERROR,
  type DraftMessages,
  type DraftResult,
  type DraftSession,
  type DraftSessionOptions,
} from './draftSession';
