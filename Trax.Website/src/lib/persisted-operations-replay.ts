// Plays back the PersistedOperations sample's recorded requests (src/data/persisted-operations-recordings.json,
// written by Trax.Samples' scripts/recordings/persisted-operations.mjs). The recording holds, for each state the
// store can be in, the exact request each action sends to the sample's API, the status and response it got, and,
// after an upload, what the store held. Taking an action replays its recorded exchange and moves the store the way
// the real one moved, so the actions can be taken in any order the page allows.

export type StoreState = "empty" | "uploaded" | "hotfixed";

export interface Exchange {
  request: { method: string; path: string; headers: Record<string, string>; body: Record<string, unknown> };
  status: number;
  response: Record<string, unknown>;
  durationMs: number;
}

export interface StoredOperation {
  id: string;
  operationName: string;
  document: string;
  shapeFingerprint: string;
  isActive: boolean;
  description: string | null;
}

export type Store = Record<string, StoredOperation | null>;

export interface RecordedAction {
  exchanges: Exchange[];
  /** What the store held after an upload that went through. */
  storeAfter?: Store;
}

export interface Recording {
  sources: { path: string; code: string }[];
  manifest: { id: string; document: string }[];
  states: Record<StoreState, { store: Store; actions: Record<string, RecordedAction> }>;
  /** The state an upload leaves the store in when it goes through. */
  leadsTo: Record<string, StoreState>;
}

export type Caller = "client" | "operator" | "anonymous";

export interface ActionInfo {
  key: string;
  label: string;
  /** Who sends it: a client with no key, the operator key, or someone with no key trying to manage. */
  as: Caller;
}

/** The actions the page offers, in the order it lists them. */
export const ACTIONS: ActionInfo[] = [
  { key: "callGreet:Alice", label: "Call greet_v1 by id (Alice)", as: "client" },
  { key: "callGreet:Eve", label: "Call greet_v1 by id (Eve)", as: "client" },
  { key: "callLookup:user-42", label: "Call lookupUser_v1 by id (user-42)", as: "client" },
  { key: "callLookup:user-7", label: "Call lookupUser_v1 by id (user-7)", as: "client" },
  { key: "inline", label: "Send an inline document", as: "client" },
  { key: "devInline", label: "Send an inline document named dev_greet", as: "client" },
  { key: "upload", label: "Upload the manifest", as: "operator" },
  { key: "hotfix", label: "Hot-fix greet_v1 (fields reordered)", as: "operator" },
  { key: "shapeEdit", label: "Edit greet_v1 so its shape changes", as: "operator" },
  { key: "uploadAnonymous", label: "Upload greet_v1 without a key", as: "anonymous" },
];

export interface Entry {
  key: string;
  /** The state the store was in when the action was taken. */
  from: StoreState;
  exchanges: Exchange[];
}

export interface Session {
  state: StoreState;
  store: Store;
  log: Entry[];
}

export function start(recording: Recording): Session {
  return { state: "empty", store: recording.states.empty.store, log: [] };
}

/** Whether the action was recorded in the session's state; the page offers only those. */
export function offered(recording: Recording, session: Session, key: string): boolean {
  return key in recording.states[session.state].actions;
}

/** Replays one action: logs its recorded exchanges and moves the store as the real one moved. */
export function act(recording: Recording, session: Session, key: string): Session {
  const recorded = recording.states[session.state].actions[key];
  if (!recorded) throw new Error(`No recording of ${key} in state ${session.state}`);
  const moved = recorded.storeAfter != null && recording.leadsTo[key] != null;
  return {
    state: moved ? recording.leadsTo[key] : session.state,
    store: recorded.storeAfter ?? session.store,
    log: [...session.log, { key, from: session.state, exchanges: recorded.exchanges }],
  };
}

export interface Outcome {
  ok: boolean;
  /** The error code the server answered with, if it refused. */
  code?: string;
  message?: string;
}

/** Whether the server did what was asked, and if not, the code and message it refused with. */
export function outcomeOf(exchange: Exchange): Outcome {
  const errors = exchange.response.errors as { message?: string; extensions?: { code?: string } }[] | undefined;
  if (errors?.length) return { ok: false, code: errors[0].extensions?.code, message: errors[0].message };
  const upload = (exchange.response.data as { operations?: { persistedOperations?: { uploadPersistedOperation?: { success: boolean; errors: { code: string; message: string }[] } } } } | undefined)
    ?.operations?.persistedOperations?.uploadPersistedOperation;
  if (upload && !upload.success) return { ok: false, code: upload.errors[0]?.code, message: upload.errors[0]?.message };
  return { ok: exchange.status < 400 };
}

export const FILES = {
  program: "Trax.Samples.PersistedOperations.Api/Program.cs",
  greet: "Trax.Samples.PersistedOperations/Trains/Greeting/Greet/GreetTrain.cs",
  lookup: "Trax.Samples.PersistedOperations/Trains/Users/LookupUser/LookupUserTrain.cs",
} as const;

/** The file and the line (by its text) the code panel shows for an action and how it went. */
export function highlightFor(entry: Entry): { file: string; needle: string } {
  const ok = entry.exchanges.every((e) => outcomeOf(e).ok);
  const key = entry.key;
  if (key.startsWith("callGreet") || key.startsWith("callLookup")) {
    if (!ok) return { file: FILES.program, needle: "opts.UseDatabase(connectionString)" };
    return key.startsWith("callGreet")
      ? { file: FILES.greet, needle: "Chain<ComposeGreetingJunction>()" }
      : { file: FILES.lookup, needle: "Chain<FetchUserJunction>()" };
  }
  switch (key) {
    case "inline":
      return { file: FILES.program, needle: ".RequirePersisted(true)" };
    case "devInline":
      return { file: FILES.program, needle: "opts.AllowOperationsMatching" };
    case "uploadAnonymous":
      return { file: FILES.program, needle: ".GateOperations(roles: DemoKeys.OperatorRole)" };
    case "shapeEdit":
      return { file: FILES.program, needle: ".UsePersistedOperations(opts =>" };
    default:
      return { file: FILES.program, needle: "opts.UseDatabase(connectionString)" };
  }
}

/**
 * The lines of a file the code panel shows, 0-based and end exclusive: Program.cs's GraphQL setup, or a train
 * from its attributes down.
 */
export function rangeOf(path: string, code: string): [number, number] {
  const lines = code.split("\n");
  if (path === FILES.program) {
    const first = lines.findIndex((l) => l.startsWith("builder.Services.AddTraxGraphQL("));
    const last = lines.findIndex((l, i) => i > first && l.trim() === ");");
    if (first >= 0 && last > first) return [first, last + 1];
  }
  const first = lines.findIndex((l) => /^\s*(\[Trax|public class)/.test(l));
  let end = lines.length;
  while (end > 0 && lines[end - 1].trim() === "") end--;
  return [Math.max(first, 0), end];
}
