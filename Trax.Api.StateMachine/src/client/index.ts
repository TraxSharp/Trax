/**
 * `@traxsharp/state-machine/client` — talking to a Trax server about a stored draft.
 *
 * The core package is pure: a reducer, a rule interpreter, and codegen, with no I/O anywhere. This
 * subpath is the one place that knows a server exists. It is a separate entry point so importing the
 * engine never drags a transport (or the `fetch` global) into a bundle that only wanted the reducer.
 *
 *   transport.ts    the generic mutations, machine-agnostic — `(machine, id)` on every call
 *   machines.ts     an executor over the typed `machines` root, for a machine the host exposes: the same
 *                   client and sessions drive it
 *   draftSession.ts one machine + one draft id, bound; every method total (never throws); and
 *                   startDraft, which creates a draft under an id the server mints
 *   machineServer.ts createMachineServer: the typed operations and the machineChanged stream of one exposed
 *                   machine, as the MachineServer a ServerMachineController (or useServerMachine, at
 *                   `@traxsharp/state-machine/react`) drives an instance through: the server owns it, the twin predicts
 *
 * Typical wiring:
 *
 *   const client  = createSnapshotClient(createHttpExecutor('/graphql'));
 *   const started = await startDraft({ client, machine: checkout, requestId: crypto.randomUUID() });
 *   if (started.ok) {
 *     const session = createDraftSession({ client, machine: checkout, id: started.id });
 *     await session.advance('Next', { requestId: crypto.randomUUID() });
 *   }
 */
export {
  createSnapshotClient,
  createHttpExecutor,
  type AdvanceOptions,
  type HttpExecutorOptions,
  type StartOptions,
  type SnapshotClient,
  type SnapshotExecutor,
  type SnapshotOperation,
  type SnapshotOutput,
  type SnapshotProblem,
} from './transport';

export {
  createMachineExecutor,
  type MachineDocument,
  type MachineExecutorOptions,
  type TypedMachineOperations,
} from './machines';

export {
  createDraftSession,
  startDraft,
  TRANSPORT_ERROR,
  type DraftMessages,
  type DraftResult,
  type DraftSession,
  type DraftSessionOptions,
  type StartDraftOptions,
  type StartResult,
} from './draftSession';

export {
  createMachineServer,
  persistedMessageReplacer,
  DEFAULT_RESUBSCRIBE_DELAYS,
  type DiscardResponse,
  type MachineServerOptions,
  type PersistedSubscriptionSocket,
  type SubscriptionSink,
  type TypedMachineServer,
} from './machineServer';

export {
  ServerMachineController,
  DEFAULT_RETRY_DELAYS,
  TRANSPORT_FAILED,
  type EventResponse,
  type GetResponse,
  type MachineServer,
  type SendOutcome,
  type ServerMachineError,
  type ServerMachineOptions,
  type ServerMachineView,
  type ServerProblem,
  type ServerSnapshot,
  type StartResponse,
  type StreamMessage,
} from '../server/serverController';
