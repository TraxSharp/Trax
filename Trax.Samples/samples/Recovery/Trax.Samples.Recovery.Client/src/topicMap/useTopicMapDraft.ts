// The topic map wizard's draft, owned by the server. The host exposes the topic map, so the page drives it through
// its own typed operations, machines { topicMap { start chooseFields build ... get } }, and its push,
// machineChanged, and nothing else: useServerMachine sends each declared event to the server, which applies it,
// stores it and answers what it stored, while the generated twin only predicts the step so the page moves at once.
// Whatever the server does to the draft (the build's outcome, the review timer, a recovery after a crash, another
// tab's step) is pushed to the page: it never polls.
//
// The host runs only persisted documents. It registers the topic map's own when it starts, and the page sends each
// by the id topic-map.documents.json lists, exported from the host's manifest for the IR the twin was generated
// from. After a deploy that changes the machine, the host answers those ids client-outdated, and the page stops and
// asks to be reloaded.
//
// What this file adds to the hook is the page's own memory: the draft id is kept in this browser, so closing the tab
// and coming back opens the same draft at the same step, and a draft the server no longer holds is forgotten and a
// new one started.

import { useCallback, useEffect, useRef, useState } from "react";
import type { TypedSnapshot } from "@trax/state-machine";
import { createMachineServer, persistedMessageReplacer } from "@trax/state-machine/client";
import { useServerMachine, type UseServerMachineResult } from "@trax/state-machine/react";
import { createClient } from "graphql-ws";
import { API_KEY, API_URL, WS_URL } from "../apollo";
import type { TopicMapSpec } from "./topic-map.contexts.g";
import documents from "./topic-map.documents.json";
import { topicMap } from "./topic-map.machine.g";

export type TopicMapSnapshot = TypedSnapshot<TopicMapSpec>;

const DRAFT_KEY = "trax-recovery.topic-map-draft";

// The request id of a start that has not answered yet. A reload while it is in flight sends the same id again,
// and the server answers with the draft the first request created rather than a second one.
const PENDING_START_KEY = "trax-recovery.topic-map-start";

// The topic map's operations by their persisted ids, and its push over one socket. Browsers cannot set headers on a
// WebSocket upgrade, so the key travels in connection_init. The socket opens with the first subscription and
// reconnects for as long as the page is open, a host restart included; each reconnect subscribes again, and the
// first event after it is the draft as it is then.
const server = createMachineServer(API_URL, {
  headers: () => ({ "X-Api-Key": API_KEY }),
  machine: topicMap,
  documents,
  socket: createClient({
    url: WS_URL,
    connectionParams: { apiKey: API_KEY },
    lazy: true,
    retryAttempts: Number.POSITIVE_INFINITY,
    jsonMessageReplacer: persistedMessageReplacer,
  }),
});

function read(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function write(key: string, value: string | null) {
  try {
    if (value == null) localStorage.removeItem(key);
    else localStorage.setItem(key, value);
  } catch {
    // A private window keeps nothing between visits; this one still works.
  }
}

// The request id of the next start: the one a reload left pending, or a new one, kept until the start answers.
function pendingStart(): string {
  const kept = read(PENDING_START_KEY);
  if (kept) return kept;
  const requestId = crypto.randomUUID();
  write(PENDING_START_KEY, requestId);
  return requestId;
}

// Which draft the page opens: the one this browser kept, or a new one started under a kept request id.
interface Opening {
  id?: string;
  startRequestId?: string;
}

function initialOpening(): Opening {
  const id = read(DRAFT_KEY);
  return id ? { id } : { startRequestId: pendingStart() };
}

export interface TopicMapDraft {
  /** The server-minted draft id, or null until the draft is started or loaded. */
  id: string | null;
  /** What the page shows: the twin's prediction while a step is in flight, else what the server stored. */
  snapshot: TopicMapSnapshot | null;
  /**
   * When the server fires the timer of the state the draft is in (an ISO-8601 instant), or null when that state has
   * none. Built's is the review deadline. The server sends it beside every snapshot, pushed ones included; the page
   * only shows it.
   */
  dueAt: string | null;
  /** The last refusal, with its code, or why the page stopped. */
  problem: string | null;
  busy: boolean;
  /** Sends one event to the server, after every earlier one has settled. */
  advance: UseServerMachineResult<TopicMapSpec>["send"];
  /** Whether the twin allows the event from what is shown. */
  can: UseServerMachineResult<TopicMapSpec>["can"];
  /**
   * Starts a new map. Once a build began, the draft is reset to the first step (`StartOver`): the server keeps a
   * draft in Building or in a build's outcome as the record of that build and refuses to discard it. Before that,
   * the draft is discarded and a new one started. A refusal of either is shown, and the draft kept.
   */
  startOver: () => void;
}

export function useTopicMapDraft(): TopicMapDraft {
  const [opening, setOpening] = useState<Opening>(initialOpening);
  const [discarding, setDiscarding] = useState(false);
  const [refused, setRefused] = useState<string | null>(null);
  const draft = useServerMachine(topicMap, { server, id: opening.id, startRequestId: opening.startRequestId });
  const restarting = useRef(false);

  // Opens a new draft under a new start request id.
  const restart = useCallback(() => {
    write(DRAFT_KEY, null);
    write(PENDING_START_KEY, null);
    setOpening({ startRequestId: pendingStart() });
  }, []);

  // A started draft is remembered, and its start is no longer pending.
  useEffect(() => {
    if (draft.id && opening.id === undefined) {
      write(DRAFT_KEY, draft.id);
      write(PENDING_START_KEY, null);
    }
  }, [draft.id, opening.id]);

  // A draft the server no longer holds (deleted elsewhere, or expired) is forgotten, and a new one started.
  useEffect(() => {
    if (draft.error?.code !== "gone" || restarting.current) return;
    restarting.current = true;
    restart();
  }, [draft.error, restart]);
  useEffect(() => {
    restarting.current = false;
  }, [opening]);

  // No draft is left behind: every visitor of the demo signs in with the same key, so the drafts they leave all count
  // against that one user's cap on the server. A draft a build began is reset in place, since the server will not
  // discard it; an earlier one is discarded. A refusal leaves the page on the draft it has, with the reason.
  const { can, send } = draft;
  const startOver = useCallback(() => {
    setRefused(null);
    if (can("StartOver")) {
      // A refusal or a transport failure shows as the hook's problem.
      void send("StartOver");
      return;
    }
    const old = draft.id;
    restarting.current = true;
    setDiscarding(true);
    void (async () => {
      let answer: { kind: "discarded" } | { kind: "problem"; code: string; message: string };
      try {
        answer = old ? await server.discard(old, crypto.randomUUID()) : { kind: "discarded" };
      } catch (error) {
        answer = { kind: "problem", code: "transport-error", message: error instanceof Error ? error.message : String(error) };
      }
      setDiscarding(false);
      // Discarded, or already gone: a new draft. Anything else: the old one stays, so no new one is started.
      if (answer.kind === "discarded" || answer.code === "not-found") {
        restart();
        return;
      }
      restarting.current = false;
      setRefused(`${answer.code}: ${answer.message}`);
    })();
  }, [can, send, draft.id, restart]);

  // A page built for another version of the machine gets the same answer to every request it could send, so it
  // drops the draft it shows, which hides every step, and sends nothing more.
  const outdated = draft.error?.code === "client-outdated";
  const shown = draft.error ?? draft.problem;

  return {
    id: draft.id,
    snapshot: outdated ? null : draft.snapshot,
    dueAt: outdated ? null : draft.dueAt,
    problem: shown ? `${shown.code}: ${shown.message}` : refused,
    busy: draft.pending || discarding,
    advance: draft.send,
    can: draft.can,
    startOver,
  };
}
