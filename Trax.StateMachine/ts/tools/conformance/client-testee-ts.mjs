import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { createClient } from "graphql-ws";
import { bundleModule } from "../lib/engine-loader.mjs";

// The TypeScript client as a client-conformance testee (conformance/client/PROTOCOL.md): a ServerMachineController
// over createMachineServer, a graphql-ws socket and the global fetch, built and driven through the package's public
// exports only.

const MISSING_DEPENDENCIES = 3;
// The name the kit's allowlist of what a client may lack knows this client by.
const CLIENT = "ts";
const clientEntry = fileURLToPath(new URL("./client-entry.ts", import.meta.url));

// A requestId minter that counts: r1, r2, ... from each open, so the host can tell which request is which. With
// `throwingAt`, its call of that number throws and mints nothing (PROTOCOL.md, open, throwingRequestId).
const counting = (throwingAt) => {
  let n = 0;
  let calls = 0;
  return () => {
    calls += 1;
    if (calls === throwingAt) throw new Error("the request id minter threw");
    return `r${++n}`;
  };
};

// The controller rethrows a listener's throw on a microtask, where a page's error handler would see it. The testee's
// throwing listener throws this one error, which is let go there: the transcript checks the controller carried on.
const LISTENER_THREW = new Error("the change listener threw");
let swallowing = false;
function swallowListenerThrows() {
  if (swallowing) return;
  swallowing = true;
  const queue = globalThis.queueMicrotask;
  globalThis.queueMicrotask = (task) =>
    queue(() => {
      try {
        task();
      } catch (err) {
        if (err !== LISTENER_THREW) throw err;
      }
    });
}

/** What the controller's view shows, in the protocol's shape. */
function viewOf(view) {
  return {
    status: view.status,
    instance: view.id,
    state: view.snapshot?.state ?? null,
    context: view.snapshot?.context ?? null,
    confirmed: view.confirmed === null ? null : { state: view.confirmed.state, context: view.confirmed.context },
    rowId: view.rowId,
    revision: view.revision,
    dueAt: view.dueAt,
    pending: view.pending,
    problem: view.problem?.code ?? null,
    error: view.error?.code ?? null,
    deleted: view.error?.deleted ?? null,
    unsaved: view.unsaved,
  };
}

function outcomeOf(outcome) {
  switch (outcome.outcome) {
    case "confirmed":
    case "synced":
      return { outcome: outcome.outcome, state: outcome.snapshot.state, revision: outcome.snapshot.revision };
    case "superseded":
    case "unreachable":
    case "exceeded":
      return { outcome: outcome.outcome };
    case "invalid":
      return outcome;
    case "stopped":
      return { outcome: "stopped", code: outcome.error.code };
    default:
      return { outcome: outcome.outcome, code: outcome.problem.code };
  }
}

/** 600 arrays nested one in another, built in memory (PROTOCOL.md, send and sync, tooDeep). */
function tooDeep() {
  let deep = [];
  for (let i = 1; i < 600; i++) deep = [deep];
  return deep;
}

/**
 * What a discard came to. The controller has no discard of its own: a page discards through the server it built, the
 * public `discard` of `createMachineServer`'s answer, under a request id it mints.
 */
async function discardOf(server, id, requestId) {
  // The discard is total: one that throws is not an outcome, and fails the transcript as the testee's error.
  const answer = await server.discard(id, requestId);
  if (answer.kind === "discarded") return { outcome: "discarded" };
  return { outcome: answer.code === "TRANSPORT_ERROR" ? "failed" : "refused", code: answer.code };
}

/** A handler from one request to a promise of its answer, over `engine`, the package's exports. */
export function createClientTestee(engine) {
  const { typedMachineFromIr, createMachineServer, persistedMessageReplacer, ServerMachineController } = engine;
  let controller = null;
  let socket = null;
  let server = null;
  let newRequestId = null;
  // The open request the client was built from, which a build overrides, and whether its headers throw.
  let opened = null;
  let headersThrow = false;

  const close = () => {
    controller?.dispose();
    controller = null;
    server = null;
    void socket?.dispose();
    socket = null;
  };

  // A server and controller from an open request, or the code it was refused with.
  const build = (r) => {
    const machine = typedMachineFromIr(r.ir, r.irHash);
    const builtSocket = createClient({
      url: r.socket,
      webSocketImpl: WebSocket,
      lazy: true,
      retryAttempts: 0,
      jsonMessageReplacer: persistedMessageReplacer,
    });
    try {
      const builtServer = createMachineServer(r.endpoint, {
        machine,
        documents: r.documents,
        socket: builtSocket,
        resubscribeDelays: r.resubscribeDelays,
        maxResponseSize: r.maxResponseSize,
        requestTimeout: r.requestTimeout,
        allowInsecureTransport: r.allowInsecureTransport,
        headers: () => {
          if (headersThrow) throw new Error("the headers provider threw");
          return {};
        },
      });
      const minter = counting(r.throwingRequestId);
      const built = new ServerMachineController({
        machine,
        server: builtServer,
        id: r.instance ?? undefined,
        startInput: r.startInput ?? undefined,
        newRequestId: minter,
        retryDelays: r.retryDelays,
      });
      return { socket: builtSocket, server: builtServer, controller: built, newRequestId: minter };
    } catch (err) {
      void builtSocket.dispose();
      // A client refusing cleartext to another host refuses to be built, before it sends anything; one refusing an
      // option out of range throws a RangeError.
      if (/not over TLS/.test(err.message)) return { code: "INSECURE_TRANSPORT" };
      if (err instanceof RangeError) return { code: "INVALID_OPTION" };
      throw err;
    }
  };

  const handlers = {
    open: async (r) => {
      close();
      headersThrow = false;
      const built = build(r);
      if (built.code !== undefined) return { opened: false, client: CLIENT, code: built.code };
      ({ socket, server, controller, newRequestId } = built);
      opened = r;
      if (r.throwingListener) {
        swallowListenerThrows();
        controller.subscribe(() => {
          throw LISTENER_THREW;
        });
      }
      // A WebSocket in a browser or in node does not say what status an upgrade it failed was answered with, and the
      // page's graphql-ws client, not this one, reads the socket's messages, closes it, and times it.
      return { opened: true, client: CLIENT, lacks: ["upgrade-status", "socket-message-limit"] };
    },
    build: async (r) => {
      const { id: _, op: __, ...options } = r;
      const built = build({ ...opened, ...options });
      if (built.code !== undefined) return { built: false, code: built.code };
      built.controller.dispose();
      void built.socket.dispose();
      return { built: true };
    },
    headers: async (r) => {
      headersThrow = r.throws === true;
      return { throws: headersThrow };
    },
    start: async () => {
      await controller.start();
      return { settled: true };
    },
    send: async (r) => {
      const input = r.tooDeep === "input" ? tooDeep() : r.input;
      return outcomeOf(await (input == null ? controller.send(r.trigger) : controller.send(r.trigger, input)));
    },
    retry: async () => outcomeOf(await controller.retry()),
    sync: async (r) => {
      const fields = r.fields ?? null;
      return outcomeOf(
        await controller.syncTo(r.target, {
          fields: r.tooDeep === "input" && fields !== null ? { ...fields, input: tooDeep() } : fields,
          navigation: r.navigation,
        }),
      );
    },
    view: async () => viewOf(controller.get()),
    can: async (r) => ({ can: r.input == null ? controller.can(r.trigger) : controller.can(r.trigger, r.input) }),
    available: async () => ({ triggers: [...controller.available()].sort() }),
    discard: async () => {
      const id = controller.get().id;
      if (id === null) return { error: "no instance to discard" };
      return discardOf(server, id, newRequestId());
    },
    dispose: async () => {
      close();
      return { disposed: true };
    },
    end: async () => {
      close();
      return {};
    },
  };

  return async (request) => {
    const handle = handlers[request.op];
    if (!handle) return { id: request.id, unsupported: true };
    if (request.op !== "open" && request.op !== "dispose" && request.op !== "end" && !controller)
      return { id: request.id, error: "no client is open" };
    try {
      return { id: request.id, ...(await handle(request)) };
    } catch (err) {
      return { id: request.id, error: `threw: ${err.message}` };
    }
  };
}

/** Bundles the engine and its client from source, as the CLI's entrypoints do, and returns their exports. */
export const loadClient = () => bundleModule(clientEntry);

async function main() {
  let engine;
  try {
    engine = await loadClient();
  } catch (err) {
    console.error(err.message);
    process.exit(/Could not load esbuild/.test(err.message) ? MISSING_DEPENDENCIES : 1);
  }
  const handle = createClientTestee(engine);
  // Lines end at "\n" only: readline would also end one at U+2028 or U+2029, which JSON writes raw inside a string.
  let rest = "";
  process.stdin.setEncoding("utf8");
  for await (const chunk of process.stdin) {
    const lines = (rest + chunk).split("\n");
    rest = lines.pop();
    for (const line of lines) {
      if (line.trim() === "") continue;
      const request = JSON.parse(line);
      // Each request is answered when it settles, not in order: a send waits on the host while the runner goes on.
      void handle(request).then((answer) => {
        process.stdout.write(`${JSON.stringify(answer)}\n`);
        if (request.op === "end") process.exit(0);
      });
    }
  }
}

if (process.argv[1] === fileURLToPath(import.meta.url)) await main();
