import { createHash } from "node:crypto";
import { createServer } from "node:http";

// The fake host the client transcripts run against: GraphQL over HTTP POST and graphql-transport-ws over a socket,
// both on one port, with node's own modules only. It answers nothing by itself. Every request and every subscription
// it receives is queued for the runner, which answers each as its transcript says, so a transcript decides exactly
// what a client hears and in what order.

const WS_GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
const PROTOCOL = "graphql-transport-ws";

/** A queue whose `take` waits for the next item, or fails after `timeout` milliseconds. */
class Inbox {
  #items = [];
  #waiters = [];

  put(item) {
    const waiter = this.#waiters.shift();
    if (waiter) waiter.resolve(item);
    else this.#items.push(item);
  }

  take(timeout, what) {
    if (this.#items.length > 0) return Promise.resolve(this.#items.shift());
    return new Promise((resolve, reject) => {
      const waiter = {
        resolve: (item) => {
          clearTimeout(timer);
          resolve(item);
        },
      };
      const timer = setTimeout(() => {
        this.#waiters.splice(this.#waiters.indexOf(waiter), 1);
        reject(new Error(`no ${what} arrived within ${timeout} ms`));
      }, timeout);
      this.#waiters.push(waiter);
    });
  }

  drain() {
    return this.#items.splice(0);
  }
}

// One text frame, unmasked, as a server sends it.
function frame(opcode, payload) {
  const body = Buffer.from(payload);
  const head =
    body.length < 126
      ? Buffer.from([0x80 | opcode, body.length])
      : body.length < 65536
        ? Buffer.from([0x80 | opcode, 126, body.length >> 8, body.length & 0xff])
        : (() => {
            const h = Buffer.alloc(10);
            h[0] = 0x80 | opcode;
            h[1] = 127;
            h.writeBigUInt64BE(BigInt(body.length), 2);
            return h;
          })();
  return Buffer.concat([head, body]);
}

// Reads every complete frame from `buffer`; returns the frames and what is left over.
function readFrames(buffer) {
  const frames = [];
  let offset = 0;
  while (buffer.length - offset >= 2) {
    const opcode = buffer[offset] & 0x0f;
    const masked = (buffer[offset + 1] & 0x80) !== 0;
    let length = buffer[offset + 1] & 0x7f;
    let at = offset + 2;
    if (length === 126) {
      if (buffer.length < at + 2) break;
      length = buffer.readUInt16BE(at);
      at += 2;
    } else if (length === 127) {
      if (buffer.length < at + 8) break;
      length = Number(buffer.readBigUInt64BE(at));
      at += 8;
    }
    const mask = masked ? buffer.subarray(at, at + 4) : null;
    if (masked) at += 4;
    if (buffer.length < at + length) break;
    const payload = Buffer.from(buffer.subarray(at, at + length));
    if (mask) for (let i = 0; i < payload.length; i++) payload[i] ^= mask[i % 4];
    frames.push({ opcode, payload });
    offset = at + length;
  }
  return { frames, rest: buffer.subarray(offset) };
}

/** One graphql-transport-ws connection: its subscriptions, and the means to answer and close them. */
class Connection {
  #socket;
  #onSubscribe;
  #acknowledged = false;
  #closed = false;
  #refuse;

  constructor(socket, refuse, onSubscribe) {
    this.#socket = socket;
    this.#refuse = refuse;
    this.#onSubscribe = onSubscribe;
    let pending = Buffer.alloc(0);
    socket.on("data", (chunk) => {
      const { frames, rest } = readFrames(Buffer.concat([pending, chunk]));
      pending = rest;
      for (const f of frames) this.#onFrame(f);
    });
    socket.on("close", () => {
      this.#closed = true;
    });
    socket.on("error", () => {
      this.#closed = true;
    });
  }

  get closed() {
    return this.#closed;
  }

  send(message) {
    if (!this.#closed) this.#socket.write(frame(0x1, JSON.stringify(message)));
  }

  /** Closes the socket with a close frame, as a host that stops or refuses does. */
  close(code = 1000, reason = "") {
    if (this.#closed) return;
    const payload = Buffer.alloc(2 + Buffer.byteLength(reason));
    payload.writeUInt16BE(code, 0);
    payload.write(reason, 2);
    this.#socket.write(frame(0x8, payload));
    this.#socket.end();
    this.#closed = true;
  }

  #onFrame({ opcode, payload }) {
    if (opcode === 0x8) return this.close();
    if (opcode === 0x9) return this.#socket.write(frame(0xa, payload));
    if (opcode !== 0x1) return;
    const message = JSON.parse(payload.toString("utf8"));
    switch (message.type) {
      case "connection_init":
        // A refused connection closes before it is acknowledged, as a host that turns the caller away does.
        if (this.#refuse) return this.close(4403, "Forbidden");
        this.#acknowledged = true;
        return this.send({ type: "connection_ack" });
      case "ping":
        return this.send({ type: "pong" });
      case "subscribe":
        if (!this.#acknowledged) return this.close(4401, "Unauthorized");
        return this.#onSubscribe(this, message);
      case "complete":
        return this.#onSubscribe(this, message);
    }
  }
}

/**
 * Starts the fake host on a free port. `documents` maps each persisted id to its operation, so a request can be
 * reported by operation. Returns its endpoints and the queues the runner answers from.
 */
export async function startHost() {
  const requests = new Inbox();
  const subscriptions = new Inbox();
  const unknown = [];
  let documents = new Map();
  let refuseConnections = 0;
  const connections = new Set();
  const live = new Map();
  // Every upgraded socket, so closing the host does not wait on a client that has not closed its end.
  const sockets = new Set();

  const server = createServer((req, res) => {
    if (req.method !== "POST") {
      res.writeHead(405).end();
      return;
    }
    let body = "";
    req.setEncoding("utf8");
    req.on("data", (chunk) => (body += chunk));
    req.on("end", () => {
      let parsed;
      try {
        parsed = JSON.parse(body);
      } catch {
        res.writeHead(400).end();
        return;
      }
      const operation = documents.get(parsed.id);
      if (operation === undefined) {
        unknown.push(parsed);
        res.writeHead(200, { "content-type": "application/json" });
        res.end(JSON.stringify({ errors: [{ message: "PersistedQueryNotFound" }] }));
        return;
      }
      requests.put({
        operation,
        body: parsed,
        respond: ({ status = 200, json, close }) => {
          if (close) {
            req.socket.destroy();
            return;
          }
          res.writeHead(status, { "content-type": "application/json" });
          res.end(json === undefined ? "" : JSON.stringify(json));
        },
      });
    });
  });

  server.on("upgrade", (req, socket) => {
    sockets.add(socket);
    socket.on("close", () => sockets.delete(socket));
    const key = req.headers["sec-websocket-key"];
    const protocols = String(req.headers["sec-websocket-protocol"] ?? "")
      .split(",")
      .map((p) => p.trim());
    if (!key || !protocols.includes(PROTOCOL)) {
      socket.end("HTTP/1.1 400 Bad Request\r\n\r\n");
      return;
    }
    const accept = createHash("sha1").update(key + WS_GUID).digest("base64");
    socket.write(
      "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
        `Sec-WebSocket-Accept: ${accept}\r\nSec-WebSocket-Protocol: ${PROTOCOL}\r\n\r\n`,
    );
    const refuse = refuseConnections > 0;
    if (refuse) refuseConnections -= 1;
    const connection = new Connection(socket, refuse, (conn, message) => {
      if (message.type === "complete") {
        live.get(conn)?.delete(message.id);
        return;
      }
      const payload = message.payload ?? {};
      const operation = documents.get(payload.id);
      const subscription = {
        operation,
        payload,
        get open() {
          return !conn.closed && (live.get(conn)?.has(message.id) ?? false);
        },
        next: (value) => conn.send({ id: message.id, type: "next", payload: value }),
        error: (errors) => {
          live.get(conn)?.delete(message.id);
          conn.send({ id: message.id, type: "error", payload: errors });
        },
        complete: () => {
          live.get(conn)?.delete(message.id);
          conn.send({ id: message.id, type: "complete" });
        },
        drop: () => conn.close(1011, "Going away"),
      };
      live.get(conn).add(message.id);
      subscriptions.put(subscription);
    });
    live.set(connection, new Set());
    connections.add(connection);
  });

  await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
  const { port } = server.address();

  return {
    endpoint: `http://127.0.0.1:${port}/graphql`,
    socket: `ws://127.0.0.1:${port}/graphql`,
    requests,
    subscriptions,
    /** Sets up the host for one transcript: its documents, and how many connections it refuses first. */
    reset({ documents: docs, refuse = 0 }) {
      documents = new Map(docs.map((d) => [d.id, d.operation]));
      refuseConnections = refuse;
      for (const c of connections) c.close(1001, "Next transcript");
      connections.clear();
      live.clear();
      requests.drain();
      subscriptions.drain();
      unknown.splice(0);
    },
    /** What arrived that no step took: requests, subscriptions, and requests for an id no document has. */
    leftovers() {
      return [
        ...requests.drain().map((r) => ({ request: r.operation, variables: r.body.variables })),
        ...subscriptions.drain().map((s) => ({ subscribe: s.operation, variables: s.payload.variables })),
        ...unknown.splice(0).map((u) => ({ unknownDocument: u.id ?? null })),
      ];
    },
    close: () =>
      new Promise((resolve) => {
        for (const c of connections) c.close(1001, "Done");
        for (const socket of sockets) socket.destroy();
        server.close(() => resolve());
        server.closeAllConnections();
      }),
  };
}
