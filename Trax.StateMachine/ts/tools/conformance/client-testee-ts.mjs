import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { createClient } from "graphql-ws";
import { bundleModule } from "../lib/engine-loader.mjs";

// The TypeScript client as a client-conformance testee (conformance/client/PROTOCOL.md): a ServerMachineController
// over createMachineServer, a graphql-ws socket and the global fetch, built and driven through the package's public
// exports only.

const MISSING_DEPENDENCIES = 3;
const clientEntry = fileURLToPath(new URL("./client-entry.ts", import.meta.url));

// A requestId minter that counts: r1, r2, ... from each open, so the host can tell which request is which.
const counting = () => {
  let n = 0;
  return () => `r${++n}`;
};

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
  };
}

function outcomeOf(outcome) {
  switch (outcome.outcome) {
    case "confirmed":
      return { outcome: "confirmed", state: outcome.snapshot.state, revision: outcome.snapshot.revision };
    case "stopped":
      return { outcome: "stopped", code: outcome.error.code };
    default:
      return { outcome: outcome.outcome, code: outcome.problem.code };
  }
}

/** A handler from one request to a promise of its answer, over `engine`, the package's exports. */
export function createClientTestee(engine) {
  const { typedMachineFromIr, createMachineServer, persistedMessageReplacer, ServerMachineController } = engine;
  let controller = null;
  let socket = null;

  const close = () => {
    controller?.dispose();
    controller = null;
    void socket?.dispose();
    socket = null;
  };

  const handlers = {
    open: async (r) => {
      close();
      const machine = typedMachineFromIr(r.ir, r.irHash);
      socket = createClient({
        url: r.socket,
        webSocketImpl: WebSocket,
        lazy: true,
        retryAttempts: 0,
        jsonMessageReplacer: persistedMessageReplacer,
      });
      let server;
      try {
        server = createMachineServer(r.endpoint, {
          machine,
          documents: r.documents,
          socket,
          resubscribeDelays: r.resubscribeDelays,
          maxResponseSize: r.maxResponseSize,
          requestTimeout: r.requestTimeout,
          allowInsecureTransport: r.allowInsecureTransport,
        });
      } catch (err) {
        // A client refusing cleartext to another host refuses to be built, before it sends anything.
        if (/not over TLS/.test(err.message)) return { opened: false, code: "INSECURE_TRANSPORT" };
        throw err;
      }
      controller = new ServerMachineController({
        machine,
        server,
        id: r.instance ?? undefined,
        startInput: r.startInput ?? undefined,
        newRequestId: counting(),
        retryDelays: r.retryDelays,
      });
      // A WebSocket in a browser or in node does not say what status an upgrade it failed was answered with.
      return { opened: true, lacks: ["upgrade-status"] };
    },
    start: async () => {
      await controller.start();
      return { settled: true };
    },
    send: async (r) => outcomeOf(await (r.input == null ? controller.send(r.trigger) : controller.send(r.trigger, r.input))),
    retry: async () => outcomeOf(await controller.retry()),
    view: async () => viewOf(controller.get()),
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
