// Records the ChatService sample for traxsharp.net, which plays it back on its landing page. Start the sample's
// host as its README says (fresh data gives a clean room list; the client is not needed), then run
//
//   node chat-service.mjs [--copy-to <dir>]
//
// It signs in as Alice and Bob with the demo keys and does what the sample's page does, over the same GraphQL
// operations and graphql-ws frames: Alice creates a room and subscribes to it, adds Bob, and Bob subscribes. Then
// every line of a small conversation tree is sent, each by its speaker, and everything both users' clients receive
// is written down with the time it arrived: the request, the response and every WebSocket frame. Then Charlie,
// who is not in the room, tries to listen, and so does a socket with no key at all.
//
// The tree's lines all go to one room, one after another. A send does not read the room's history, so each line's
// recording is the same whichever lines a reader picks before it; the player strings together the ones picked.
//
// Last, Alice adds Charlie. His client loads the room's history when he joins, and that history is the path the
// reader took, so the join is recorded once at every point of the conversation: each in a room of its own whose
// history is the path to that point, followed by every line that can come after it, with Charlie listening too.
// The website copy names those rooms by the first room's id, so the room on screen never changes.
//
// Everything lands in out/chat-service.json. --copy-to also writes <dir>/chat-service-recordings.json for the
// website, with the C# files its code panel shows; --copy-only skips recording and rewrites that file from out/.
// CHAT_HOST points the script at a host on another address than localhost:5210.

import { createClient } from "graphql-ws";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const here = dirname(fileURLToPath(import.meta.url));
const HOST = process.env.CHAT_HOST ?? "localhost:5210";
const KEYS = {
  Alice: "alice-key-do-not-use-in-production",
  Bob: "bob-key-do-not-use-in-production",
  Charlie: "charlie-key-do-not-use-in-production",
};
const ROOM_NAME = "Release planning";

// The conversation: each line names the line it answers ("" for the first turn).
const TREE = [
  { key: "a", parent: "", speaker: "Alice", content: "Core and Effect are released. Scheduler next?" },
  { key: "b", parent: "", speaker: "Alice", content: "Is anyone looking at the flaky dashboard test?" },
  { key: "a1", parent: "a", speaker: "Bob", content: "Cutting it now. Api and Dashboard after that." },
  { key: "a2", parent: "a", speaker: "Bob", content: "Give me ten minutes, the pins need bumping first." },
  { key: "b1", parent: "b", speaker: "Bob", content: "On it. It was a slow first connect in CI." },
  { key: "b2", parent: "b", speaker: "Bob", content: "Not yet. Can you open an issue?" },
  { key: "a1x", parent: "a1", speaker: "Alice", content: "Great, I'll bump the pins in Samples." },
  { key: "a1y", parent: "a1", speaker: "Alice", content: "Ping me when Api is out." },
  { key: "a2x", parent: "a2", speaker: "Alice", content: "No rush. Which pins?" },
  { key: "a2y", parent: "a2", speaker: "Alice", content: "I can bump them if you like." },
  { key: "b1x", parent: "b1", speaker: "Alice", content: "Nice catch, thanks!" },
  { key: "b1y", parent: "b1", speaker: "Alice", content: "Will the fix ship with the next patch?" },
  { key: "b2x", parent: "b2", speaker: "Alice", content: "Done, it's in the tracker." },
  { key: "b2y", parent: "b2", speaker: "Alice", content: "I'll take it after lunch." },
];

// The documents the sample's client sends (Trax.Samples.ChatService.Client/src/graphql).
const ops = {
  CreateChatRoom: `mutation CreateChatRoom($input: CreateChatRoomInput!) {
  dispatch { createChatRoom(input: $input) { externalId output { chatRoomId name createdAt } } }
}`,
  InviteToChatRoom: `mutation InviteToChatRoom($input: InviteToChatRoomInput!) {
  dispatch { inviteToChatRoom(input: $input) { externalId output { chatRoomId userId displayName invitedByDisplayName } } }
}`,
  SendMessage: `mutation SendMessage($input: SendMessageInput!) {
  dispatch { sendMessage(input: $input) { externalId output { messageId chatRoomId senderUserId senderDisplayName content sentAt } } }
}`,
  GetChatHistory: `query GetChatHistory($input: GetChatHistoryInput!) {
  discover { getChatHistory(input: $input) { messages { id senderUserId senderDisplayName content sentAt } } }
}`,
  OnChatEvent: `subscription OnChatEvent($chatRoomId: UUID!) {
  onChatEvent(chatRoomId: $chatRoomId) { chatRoomId eventType payload timestamp trainExternalId }
}`,
};

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** The key is a credential, demo or not: the recording keeps that one was sent, never its value. */
const mask = (key) => `${key.slice(0, 6)}•••`;

// Entries go to the segment being recorded; t is milliseconds from the segment's start.
let segment = null;
const log = (entry) => segment.entries.push({ t: Date.now() - segment.t0, ...entry });
const begin = (meta) => (segment = { ...meta, t0: Date.now(), entries: [] });
const end = () => {
  const { t0, ...done } = segment;
  segment = null;
  return done;
};

async function http(user, operationName, variables) {
  const query = ops[operationName];
  log({ channel: "http", direction: "out", user, op: operationName, detail: { query, variables } });
  const started = Date.now();
  const response = await fetch(`http://${HOST}/trax/graphql`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-Api-Key": KEYS[user] },
    body: JSON.stringify({ operationName, query, variables }),
  });
  const body = await response.json();
  log({ channel: "http", direction: "in", user, op: operationName, status: response.status, durationMs: Date.now() - started, detail: body });
  return body;
}

/** A WebSocket that writes every frame it sends and receives, and its opening and closing, into the segment. */
function recordingSocket(user) {
  return class RecordingWebSocket extends WebSocket {
    constructor(url, protocols) {
      super(url, protocols);
      log({ channel: "ws", direction: "info", user, frame: "opening", detail: { url: String(url) } });
      this.addEventListener("message", (e) => frame(user, "in", String(e.data)));
      this.addEventListener("close", (e) => segment && log({ channel: "ws", direction: "info", user, frame: "closed", code: e.code, detail: { code: e.code, reason: e.reason } }));
    }
    send(data) {
      frame(user, "out", String(data));
      super.send(data);
    }
  };
}

function frame(user, direction, raw) {
  const parsed = JSON.parse(raw);
  if (parsed.type === "ping" || parsed.type === "pong" || !segment) return;
  if (parsed.type === "connection_init" && typeof parsed.payload?.apiKey === "string") parsed.payload.apiKey = mask(parsed.payload.apiKey);
  log({ channel: "ws", direction, user, frame: parsed.type, detail: parsed });
}

/** Subscribes a user to the room. Resolves once the server has had the subscribe frame and answered or stayed quiet. */
function subscribe(user, roomId, received, options = {}) {
  const client = createClient({
    url: `ws://${HOST}/trax/graphql`,
    webSocketImpl: recordingSocket(user),
    connectionParams: options.noKey ? {} : { apiKey: KEYS[user] },
    retryAttempts: 0,
    lazy: true,
  });
  client.subscribe(
    { query: ops.OnChatEvent, variables: { chatRoomId: roomId } },
    {
      next: ({ data }) => data?.onChatEvent && received.push({ user, event: data.onChatEvent }),
      error: () => {},
      complete: () => {},
    },
  );
  return client;
}

async function until(test, ms = 5000) {
  const deadline = Date.now() + ms;
  while (!test()) {
    if (Date.now() > deadline) throw new Error("timed out waiting for the host");
    await sleep(25);
  }
}

async function record() {
  const received = [];
  const clients = [];

  // Alice creates the room, subscribes, and adds Bob; then Bob subscribes.
  begin({ key: "prologue" });
  const created = await http("Alice", "CreateChatRoom", { input: { name: ROOM_NAME } });
  const roomId = created.data.dispatch.createChatRoom.output.chatRoomId;
  clients.push(subscribe("Alice", roomId, received));
  await until(() => segment.entries.some((e) => e.user === "Alice" && e.frame === "subscribe"));
  await sleep(300);
  const invited = await http("Alice", "InviteToChatRoom", { input: { chatRoomId: roomId, userId: "TraxApiKey:bob" } });
  const inviteRun = invited.data.dispatch.inviteToChatRoom.externalId;
  await until(() => received.some((r) => r.event.trainExternalId === inviteRun));
  await http("Bob", "GetChatHistory", { input: { chatRoomId: roomId, take: 100 } });
  clients.push(subscribe("Bob", roomId, received));
  await until(() => segment.entries.some((e) => e.user === "Bob" && e.frame === "subscribe"));
  await sleep(300);
  const prologue = end();

  // Every line of the tree, sent by its speaker; both subscribers' frames are recorded with it.
  const lines = [];
  for (const line of TREE) {
    begin({ ...line });
    const sent = await http(line.speaker, "SendMessage", { input: { chatRoomId: roomId, content: line.content } });
    const run = sent.data.dispatch.sendMessage.externalId;
    await until(() => ["Alice", "Bob"].every((u) => received.some((r) => r.user === u && r.event.trainExternalId === run)));
    await sleep(150);
    lines.push(end());
    console.log(`recorded ${line.key}: ${line.speaker}: ${line.content}`);
  }

  // Charlie is not in the room: his history read and his subscription are refused. A socket with no key is closed.
  begin({ key: "refusal" });
  await http("Charlie", "GetChatHistory", { input: { chatRoomId: roomId, take: 100 } });
  clients.push(subscribe("Charlie", roomId, received));
  await until(() => segment.entries.some((e) => e.user === "Charlie" && e.frame === "error"));
  await sleep(200);
  clients.push(subscribe("Anonymous", roomId, received, { noKey: true }));
  await until(() => segment.entries.some((e) => e.user === "Anonymous" && e.frame === "closed"));
  await sleep(200);
  const refusal = end();

  for (const client of clients) await client.dispose();

  const charlie = { joins: [], lines: [], rooms: [] };
  for (const node of ["", ...TREE.map((l) => l.key)]) {
    // A room whose history is the path to this point. Setting it up is not recorded.
    begin({ key: "setup" });
    const room = await http("Alice", "CreateChatRoom", { input: { name: ROOM_NAME } });
    const id = room.data.dispatch.createChatRoom.output.chatRoomId;
    charlie.rooms.push(id);
    const heard = [];
    const listeners = [subscribe("Alice", id, heard)];
    await until(() => segment.entries.some((e) => e.user === "Alice" && e.frame === "subscribe"));
    await sleep(300);
    const added = await http("Alice", "InviteToChatRoom", { input: { chatRoomId: id, userId: "TraxApiKey:bob" } });
    await until(() => heard.some((r) => r.event.trainExternalId === added.data.dispatch.inviteToChatRoom.externalId));
    listeners.push(subscribe("Bob", id, heard));
    await until(() => segment.entries.some((e) => e.user === "Bob" && e.frame === "subscribe"));
    await sleep(300);
    for (const line of pathTo(node)) {
      const sent = await http(line.speaker, "SendMessage", { input: { chatRoomId: id, content: line.content } });
      const run = sent.data.dispatch.sendMessage.externalId;
      await until(() => ["Alice", "Bob"].every((u) => heard.some((r) => r.user === u && r.event.trainExternalId === run)));
    }
    end();

    // Alice adds Charlie; his client loads the history and subscribes.
    begin({ key: node });
    const invited = await http("Alice", "InviteToChatRoom", { input: { chatRoomId: id, userId: "TraxApiKey:charlie" } });
    const run = invited.data.dispatch.inviteToChatRoom.externalId;
    await until(() => ["Alice", "Bob"].every((u) => heard.some((r) => r.user === u && r.event.trainExternalId === run)));
    await http("Charlie", "GetChatHistory", { input: { chatRoomId: id, take: 100 } });
    listeners.push(subscribe("Charlie", id, heard));
    await until(() => segment.entries.some((e) => e.user === "Charlie" && e.frame === "subscribe"));
    await sleep(300);
    charlie.joins.push(end());

    // Every line that can follow, with all three listening.
    for (const line of TREE.filter((l) => l.key !== node && pathTo(l.key).some((p) => p.key === node || node === ""))) {
      begin({ ...line, key: `${node}:${line.key}`, line: line.key });
      const sent = await http(line.speaker, "SendMessage", { input: { chatRoomId: id, content: line.content } });
      const sentRun = sent.data.dispatch.sendMessage.externalId;
      await until(() => ["Alice", "Bob", "Charlie"].every((u) => heard.some((r) => r.user === u && r.event.trainExternalId === sentRun)));
      await sleep(150);
      charlie.lines.push(end());
    }
    for (const listener of listeners) await listener.dispose();
    console.log(`recorded Charlie joining after "${node || "(start)"}"`);
  }

  return { recordedAt: new Date().toISOString(), roomName: ROOM_NAME, prologue, lines, refusal, charlie };
}

/** The lines from the first turn down to `key`, in order; none for "". */
function pathTo(key) {
  const path = [];
  for (let k = key; k; ) {
    const line = TREE.find((l) => l.key === k);
    path.unshift(line);
    k = line.parent;
  }
  return path;
}

const SOURCES = [
  "Trax.Samples.ChatService/Trains/SendMessage/SendMessageTrain.cs",
  "Trax.Samples.ChatService/Trains/CreateChatRoom/CreateChatRoomTrain.cs",
  "Trax.Samples.ChatService/Trains/InviteToChatRoom/InviteToChatRoomTrain.cs",
  "Trax.Samples.ChatService/Hooks/ChatLifecycleHook.cs",
  "Trax.Samples.ChatService/Subscriptions/ChatSubscriptions.cs",
];

function writeWebsiteCopy(dir) {
  const recording = JSON.parse(readFileSync(join(here, "out", "chat-service.json"), "utf8"));
  const sample = resolve(here, "../../samples/ChatService");
  const sources = SOURCES.map((path) => ({ path, code: readFileSync(join(sample, path), "utf8").replace(/\r/g, "") }));
  mkdirSync(dir, { recursive: true });
  const file = join(dir, "chat-service-recordings.json");
  // The socket URLs name the port the recording host ran on; the copy shows the sample's own, 5210. Charlie's rooms
  // are named by the first room's id, so the room on screen stays the same.
  const roomId = recording.prologue.entries.find((e) => e.op === "CreateChatRoom" && e.direction === "in").detail.data.dispatch
    .createChatRoom.output.chatRoomId;
  const { rooms, ...charlie } = recording.charlie;
  let json = JSON.stringify({ sources, ...recording, charlie }).replace(/ws:\/\/localhost:\d+/g, "ws://localhost:5210");
  for (const room of rooms) json = json.replaceAll(room, roomId);
  writeFileSync(file, json + "\n");
  console.log(`wrote ${file}`);
}

async function main() {
  const args = process.argv.slice(2);
  const copyAt = args.indexOf("--copy-to");
  const copyTo = copyAt >= 0 ? resolve(process.cwd(), args[copyAt + 1]) : null;
  if (args.includes("--copy-only")) {
    if (!copyTo) throw new Error("--copy-only needs --copy-to <dir>");
    return writeWebsiteCopy(copyTo);
  }
  const recording = await record();
  mkdirSync(join(here, "out"), { recursive: true });
  writeFileSync(join(here, "out", "chat-service.json"), JSON.stringify(recording, null, 2));
  if (copyTo) writeWebsiteCopy(copyTo);
}

main().catch((error) => {
  console.error(error);
  process.exit(1);
});
