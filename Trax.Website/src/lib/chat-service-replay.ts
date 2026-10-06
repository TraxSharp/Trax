// Plays back the ChatService sample's recorded traffic (src/data/chat-service-recordings.json, written by
// Trax.Samples' scripts/recordings/chat-service.mjs). The recording is what Alice's and Bob's clients sent and
// received: every GraphQL request and response, and every graphql-ws frame, with the time each arrived.
//
// It has four parts: the prologue (Alice creates a room, subscribes and adds Bob; Bob loads the history and
// subscribes), one segment per line of a small conversation tree, a refusal (Charlie, who is not in the room, tries
// to listen, and so does a socket with no key), and Charlie joining. Every line was sent to the same room by its
// speaker, so a reader can pick any path through the tree and each line plays exactly as the server answered it.
//
// Charlie's client loads the room's history when he joins, so his join was recorded at every point of the
// conversation, each followed by every line that can come after it with Charlie listening too. Once he has joined,
// the player plays those instead.

export type Channel = "http" | "ws";
export type Direction = "out" | "in" | "info";

export interface Entry {
  /** Milliseconds from the start of its segment. */
  t: number;
  channel: Channel;
  direction: Direction;
  user: string;
  /** The GraphQL operation, for an HTTP request or response. */
  op?: string;
  /** The graphql-ws frame type, or "opening" and "closed" for the socket itself. */
  frame?: string;
  status?: number;
  durationMs?: number;
  code?: number;
  detail?: unknown;
}

export interface Segment {
  key: string;
  /** The line this one answers, "" for the first turn. Only conversation lines have one. */
  parent?: string;
  speaker?: string;
  content?: string;
  entries: Entry[];
}

export interface ChatRecording {
  sources: { path: string; code: string }[];
  roomName: string;
  prologue: Segment;
  lines: Segment[];
  refusal: Segment;
  charlie: {
    /** Alice adds Charlie, and his client loads the history and subscribes; keyed by the last line before it. */
    joins: Segment[];
    /** Each line that can follow a join, with Charlie listening; keyed "<join>:<line>". */
    lines: Segment[];
  };
}

export interface Played extends Entry {
  id: number;
  segment: string;
}

export interface ChatPlayback {
  /** Entries waiting to be shown, each with the time it is due. */
  queue: { entry: Entry; segment: string; due: number }[];
  played: Played[];
  /** The conversation lines picked so far, in order. */
  path: string[];
  refused: boolean;
  /** The line Charlie joined after ("" for before the first), or null while he is not in the room. */
  charlieJoinedAt: string | null;
}

/**
 * The least time between two entries appearing. Over loopback the whole send, response and both events arrive
 * within a few milliseconds, too fast to follow; the recorded times and durations are still the ones shown.
 */
export const PACE_MS = 280;

export function begin(recording: ChatRecording, now: number): ChatPlayback {
  return enqueue({ queue: [], played: [], path: [], refused: false, charlieJoinedAt: null }, recording.prologue, now);
}

function enqueue(p: ChatPlayback, segment: Segment, now: number): ChatPlayback {
  let due = Math.max(now, p.queue.at(-1)?.due ?? now);
  const start = due;
  const queue = [...p.queue];
  segment.entries.forEach((entry, i) => {
    due = Math.max(start + entry.t, i === 0 ? due : due + PACE_MS);
    queue.push({ entry, segment: segment.key, due });
  });
  return { ...p, queue };
}

/** Moves every entry due by `now` from the queue to what has been shown. */
export function advance(p: ChatPlayback, now: number): ChatPlayback {
  if (p.queue.length === 0 || p.queue[0].due > now) return p;
  const played = [...p.played];
  let i = 0;
  while (i < p.queue.length && p.queue[i].due <= now) {
    const { entry, segment } = p.queue[i++];
    played.push({ ...entry, segment, id: played.length });
  }
  return { ...p, queue: p.queue.slice(i), played };
}

export const busy = (p: ChatPlayback) => p.queue.length > 0;

/** The lines a reader can send next: none while something is still playing. */
export function choices(recording: ChatRecording, p: ChatPlayback): Segment[] {
  if (busy(p)) return [];
  const parent = p.path.at(-1) ?? "";
  return recording.lines.filter((l) => l.parent === parent);
}

export function choose(recording: ChatRecording, p: ChatPlayback, key: string, now: number): ChatPlayback | null {
  const line = choices(recording, p).find((l) => l.key === key);
  if (!line) return null;
  // With Charlie in the room, the same line as it was sent with him listening too.
  const played =
    p.charlieJoinedAt == null ? line : recording.charlie.lines.find((l) => l.key === `${p.charlieJoinedAt}:${key}`);
  if (!played) return null;
  return { ...enqueue(p, played, now), path: [...p.path, key] };
}

/**
 * Lets Charlie, then a socket with no key, try to listen to the room. Once, while Charlie is not in it, and not while
 * something plays.
 */
export function refuse(recording: ChatRecording, p: ChatPlayback, now: number): ChatPlayback | null {
  if (busy(p) || p.refused || p.charlieJoinedAt != null) return null;
  return { ...enqueue(p, recording.refusal, now), refused: true };
}

/** Alice adds Charlie to the room, at whatever point the conversation has reached. Once, and not while something plays. */
export function addCharlie(recording: ChatRecording, p: ChatPlayback, now: number): ChatPlayback | null {
  if (busy(p) || p.charlieJoinedAt != null) return null;
  const at = p.path.at(-1) ?? "";
  const join = recording.charlie.joins.find((j) => j.key === at);
  if (!join) return null;
  return { ...enqueue(p, join, now), charlieJoinedAt: at };
}

export interface ChatEvent {
  chatRoomId: string;
  eventType: string;
  payload: string;
  timestamp: string;
  trainExternalId: string;
}

/** The onChatEvent a frame carries, if it is a subscription's next. */
export function eventOf(entry: Entry): ChatEvent | null {
  if (entry.channel !== "ws" || entry.frame !== "next") return null;
  const detail = entry.detail as { payload?: { data?: { onChatEvent?: ChatEvent } } };
  return detail?.payload?.data?.onChatEvent ?? null;
}

/** The run id an HTTP response names: dispatch.<train>.externalId. */
function runOf(entry: Entry): string | null {
  if (entry.channel !== "http" || entry.direction !== "in") return null;
  const data = (entry.detail as { data?: Record<string, Record<string, { externalId?: string }>> })?.data;
  const dispatch = data?.dispatch;
  if (!dispatch) return null;
  return Object.values(dispatch)[0]?.externalId ?? null;
}

export interface ViewLine {
  key: string;
  kind: "message" | "system";
  sender?: string;
  content: string;
  mine?: boolean;
  sentAt?: string;
}

/**
 * What one user's chat shows: the room once Alice created it or the user's history loaded, the history itself, and
 * every event that user's own subscription received. A message appears when its MessageSent event arrives, the
 * sender's included. A refused history read or subscription shows as a line of its own.
 */
export function viewOf(p: ChatPlayback, user: string): { room: boolean; subscribed: boolean; lines: ViewLine[] } {
  const lines: ViewLine[] = [];
  let room = false;
  let subscribed = false;
  for (const entry of p.played) {
    if (entry.user !== user) continue;
    if (entry.op === "CreateChatRoom" && entry.direction === "in") room = true;
    if (entry.op === "GetChatHistory" && entry.direction === "in") {
      const body = entry.detail as {
        errors?: { message: string }[];
        data?: { discover?: { getChatHistory?: { messages: { id: string; senderDisplayName: string; content: string; sentAt: string }[] } } };
      };
      if (body.errors?.length) {
        lines.push({ key: `history-refused:${entry.id}`, kind: "system", content: `Trax refused the history: ${body.errors[0].message}` });
        continue;
      }
      room = true;
      const messages = body.data?.discover?.getChatHistory?.messages ?? [];
      for (const m of messages)
        lines.push({ key: `history:${m.id}`, kind: "message", sender: m.senderDisplayName, content: m.content, mine: m.senderDisplayName === user, sentAt: m.sentAt });
      lines.push({
        key: `history-loaded:${entry.id}`,
        kind: "system",
        content: messages.length ? `${messages.length} earlier message${messages.length === 1 ? "" : "s"} loaded` : "No messages yet",
      });
      continue;
    }
    if (entry.frame === "subscribe") subscribed = true;
    if (entry.frame === "error") {
      subscribed = false;
      const errors = (entry.detail as { payload?: { message: string }[] }).payload;
      lines.push({ key: `subscribe-refused:${entry.id}`, kind: "system", content: `Trax refused the subscription: ${errors?.[0]?.message ?? "refused"}` });
      continue;
    }
    const event = eventOf(entry);
    if (!event) continue;
    const payload = JSON.parse(event.payload) as Record<string, string>;
    if (event.eventType === "MessageSent")
      lines.push({
        key: event.trainExternalId,
        kind: "message",
        sender: payload.senderDisplayName,
        content: payload.content,
        mine: payload.senderDisplayName === user,
        sentAt: payload.sentAt,
      });
    else if (event.eventType === "UserJoined")
      lines.push({
        key: event.trainExternalId,
        kind: "system",
        content: payload.invitedByDisplayName
          ? `${payload.invitedByDisplayName} added ${payload.displayName}`
          : `${payload.displayName} joined`,
      });
  }
  return { room, subscribed, lines };
}

const MUTATIONS = new Set(["CreateChatRoom", "InviteToChatRoom", "SendMessage"]);
const lowerFirst = (s: string) => s.charAt(0).toLowerCase() + s.slice(1);

export interface Described {
  title: string;
  note?: string;
  tone: "out" | "in" | "event" | "refused" | "info";
}

/** What an entry is and what Trax did, in a sentence, the way the sample's own Under the hood panel says it. */
export function describe(entry: Played, played: Played[]): Described {
  if (entry.channel === "http") {
    if (entry.direction === "out") {
      const kind = MUTATIONS.has(entry.op ?? "") ? "mutation" : "query";
      const field = lowerFirst(entry.op ?? "");
      return {
        title: `POST ${kind} ${entry.op}`,
        note:
          kind === "mutation"
            ? `Asks Trax to run the ${entry.op} train now (dispatch.${field}).`
            : `Runs the ${entry.op} query train (discover.${field}).`,
        tone: "out",
      };
    }
    const errors = (entry.detail as { errors?: { message: string }[] })?.errors;
    if (errors?.length)
      return { title: `Errors ${entry.op}`, note: errors.map((e) => e.message).join("; "), tone: "refused" };
    const run = runOf(entry);
    return {
      title: `${entry.status ?? 200} OK ${entry.op}`,
      note: run ? `The ${entry.op} train completed; Trax recorded the run as ${run.slice(0, 8)}.` : undefined,
      tone: "in",
    };
  }

  const event = eventOf(entry);
  if (event) {
    const source = played.find((e) => runOf(e) === event.trainExternalId);
    const train = source?.op ?? "chat";
    return {
      title: `next · ${event.eventType}`,
      note: `When the ${train} train finished, ChatLifecycleHook published ${event.eventType} to this room's topic; same run ${event.trainExternalId.slice(0, 8)}.`,
      tone: "event",
    };
  }

  const detail = entry.detail as { url?: string; reason?: string; payload?: unknown; type?: string };
  switch (entry.frame) {
    case "opening":
      return { title: "Opening WebSocket", note: `${detail?.url} (graphql-transport-ws)`, tone: "info" };
    case "closed":
      return {
        title: `WebSocket closed (${entry.code})`,
        note:
          entry.code === 4403 ? "Trax closed the socket: no valid API key in connection_init." : detail?.reason || undefined,
        tone: entry.code === 4403 ? "refused" : "info",
      };
    case "connection_init": {
      const key = (detail?.payload as { apiKey?: string } | undefined)?.apiKey;
      return {
        title: "connection_init",
        note: key
          ? "Sends the API key in the first frame: a browser cannot set headers on a WebSocket upgrade."
          : "Sends no API key.",
        tone: "out",
      };
    }
    case "connection_ack":
      return { title: "connection_ack", note: "Trax authenticated the socket from the key in connection_init.", tone: "in" };
    case "subscribe": {
      const room = (detail?.payload as { variables?: { chatRoomId?: string } })?.variables?.chatRoomId ?? "";
      return {
        title: "subscribe",
        note: `Subscribes to onChatEvent for room ${room.slice(0, 8)}; Trax checks the caller is in the room.`,
        tone: "out",
      };
    }
    case "error": {
      const errors = detail?.payload as { message: string }[] | undefined;
      return {
        title: "error",
        note: `Trax refused the subscription: ${errors?.map((e) => e.message).join("; ") ?? "refused"}`,
        tone: "refused",
      };
    }
    default:
      return { title: entry.frame ?? "frame", tone: entry.direction === "out" ? "out" : "in" };
  }
}

/** The file and the line of it an entry exercised, for the code panel. Null when none of the shown files is. */
export function focusOf(entry: Played): { file: string; needle: string } | null {
  if (entry.channel === "http") {
    if (entry.op === "SendMessage") return { file: "SendMessageTrain.cs", needle: "Chain<ValidateSenderJunction>" };
    if (entry.op === "CreateChatRoom") return { file: "CreateChatRoomTrain.cs", needle: "Chain<ValidateInputJunction>" };
    if (entry.op === "InviteToChatRoom") return { file: "InviteToChatRoomTrain.cs", needle: "Chain<ValidateInviteJunction>" };
    return null;
  }
  if (eventOf(entry)) return { file: "ChatLifecycleHook.cs", needle: "eventSender.SendAsync" };
  if (entry.frame === "subscribe") return { file: "ChatSubscriptions.cs", needle: "isParticipant =" };
  if (entry.frame === "error") return { file: "ChatSubscriptions.cs", needle: "throw new GraphQLException" };
  return null;
}

/** The 0-based line of `code` that contains `needle`, or -1. */
export function lineOf(code: string, needle: string): number {
  return code.split("\n").findIndex((l) => l.includes(needle));
}
