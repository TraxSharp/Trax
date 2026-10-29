import { fileURLToPath } from "node:url";
import { bundleModule } from "../lib/engine-loader.mjs";

// The TypeScript operations client as a testee of the operations kit (conformance/operations/PROTOCOL.md): the client
// `trax client --operations` generated for the kit's fixture, committed under operations/client/, driven through the
// functions and the transport its index exports, over the global fetch and WebSocket.

const MISSING_DEPENDENCIES = 3;
const clientIndex = fileURLToPath(new URL("./operations/client/index.g.ts", import.meta.url));

/** Bundles the generated client, so its extensionless imports and graphql-ws resolve, and returns its exports. */
export const loadOperationsClient = () => bundleModule(clientIndex);

// The function the client generated for a document: its name with the first letter lowered.
const functionOf = (client, operation) => {
  const fn = client[operation[0].toLowerCase() + operation.slice(1)];
  if (typeof fn !== "function") throw new Error(`The client has no function for ${operation}.`);
  return fn;
};

/** A handler from one request to a promise of its answer, over `client`, the generated client's exports. */
export function createOperationsTestee(client) {
  const { configureTraxOperations, httpTransport, TraxOperationError, TraxTransportError } = client;
  let transport = null;
  let stop = null;
  let events = [];

  const outcomeOf = (error) => {
    if (error instanceof TraxOperationError) return { outcome: "refused", code: error.code ?? null };
    if (error instanceof TraxTransportError) return { outcome: "failed", code: "TRANSPORT_ERROR", status: error.status ?? null };
    // Neither of the client's errors: the caller cannot tell a refusal from a failure.
    return { outcome: "threw", message: String(error) };
  };

  const endedOf = (error) => {
    if (error instanceof TraxOperationError) return { ended: "refused", code: error.code ?? null };
    // graphql-ws ends a stream whose socket closed for good with the close event.
    if (typeof error?.code === "number" && typeof error?.reason === "string") return { ended: "closed", close: error.code };
    return { ended: "failed" };
  };

  const close = () => {
    stop?.();
    stop = null;
    transport?.dispose();
    transport = null;
    events = [];
  };

  const handlers = {
    open: (r) => {
      close();
      try {
        transport = httpTransport({
          url: r.endpoint,
          socketUrl: r.socket,
          ...(r.maxResponseSize !== undefined ? { maxResponseBytes: r.maxResponseSize } : {}),
          ...(r.requestTimeout !== undefined ? { requestTimeout: r.requestTimeout } : {}),
          ...(r.allowInsecureTransport !== undefined ? { allowInsecureTransport: r.allowInsecureTransport } : {}),
        });
      } catch {
        return { opened: false, code: "INSECURE_TRANSPORT" };
      }
      configureTraxOperations(transport);
      return { opened: true };
    },
    send: async (r) => {
      try {
        return { outcome: "data", data: await functionOf(client, r.operation)(r.variables ?? {}) };
      } catch (error) {
        return outcomeOf(error);
      }
    },
    subscribe: (r) => {
      stop?.();
      events = [];
      stop = functionOf(client, r.operation)(r.variables ?? {}, {
        next: (data) => events.push({ next: data }),
        error: (error) => events.push(endedOf(error)),
        complete: () => events.push({ ended: "complete" }),
      });
      return { subscribed: true };
    },
    events: () => ({ events: [...events] }),
    unsubscribe: () => {
      stop?.();
      stop = null;
      return { unsubscribed: true };
    },
    dispose: () => {
      close();
      return { disposed: true };
    },
    end: () => {
      close();
      return {};
    },
  };

  return async (request) => {
    const handler = handlers[request.op];
    if (!handler) return { id: request.id, unsupported: true };
    try {
      return { id: request.id, ...(await handler(request)) };
    } catch (err) {
      return { id: request.id, error: String(err?.stack ?? err) };
    }
  };
}

async function main() {
  let client;
  try {
    client = await loadOperationsClient();
  } catch (err) {
    console.error(err.message);
    process.exit(/Could not load esbuild/.test(err.message) ? MISSING_DEPENDENCIES : 1);
  }
  const handle = createOperationsTestee(client);
  // Lines end at "\n" only: readline would also end one at U+2028 or U+2029, which JSON writes raw inside a string.
  let rest = "";
  process.stdin.setEncoding("utf8");
  for await (const chunk of process.stdin) {
    const lines = (rest + chunk).split("\n");
    rest = lines.pop();
    for (const line of lines) {
      if (line.trim() === "") continue;
      const request = JSON.parse(line);
      // Each request is answered when it settles: a send waits on the host while the runner goes on.
      void handle(request).then((answer) => {
        process.stdout.write(`${JSON.stringify(answer)}\n`);
        if (request.op === "end") process.exit(0);
      });
    }
  }
}

if (process.argv[1] === fileURLToPath(import.meta.url)) await main();
