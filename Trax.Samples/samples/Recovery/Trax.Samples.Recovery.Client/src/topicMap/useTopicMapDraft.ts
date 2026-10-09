// The topic map wizard's draft: the generated twin decides locally what each step may do, and the
// server, through the four generic stateMachine mutations, decides what is stored. The draft id is
// kept in this browser, so closing the tab and coming back loads the same draft at the same step.

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type { TypedSnapshot } from "@trax/state-machine";
import { createDraftSession, createHttpExecutor, createSnapshotClient } from "@trax/state-machine/client";
import { API_KEY, API_URL } from "../apollo";
import type { TopicMapSpec, TopicMapTrigger } from "./topic-map.contexts.g";
import { topicMap } from "./topic-map.machine.g";

export type TopicMapSnapshot = TypedSnapshot<TopicMapSpec>;

const DRAFT_KEY = "trax-recovery.topic-map-draft";

const client = createSnapshotClient(
  createHttpExecutor(API_URL, { headers: () => ({ "X-Api-Key": API_KEY }) }),
);

function storedDraftId(): string {
  try {
    const stored = localStorage.getItem(DRAFT_KEY);
    if (stored) return stored;
    const fresh = crypto.randomUUID();
    localStorage.setItem(DRAFT_KEY, fresh);
    return fresh;
  } catch {
    return crypto.randomUUID();
  }
}

export interface TopicMapDraft {
  id: string;
  snapshot: TopicMapSnapshot | null;
  problem: string | null;
  busy: boolean;
  /** Fires a trigger on the server, after the twin says it may. */
  advance: <T extends TopicMapTrigger>(trigger: T, input?: TopicMapSpec["triggers"][T]) => Promise<void>;
  /** Whether the twin allows the trigger from where the draft is now. */
  can: (trigger: TopicMapTrigger, input?: unknown) => boolean;
  /** Starts a new draft, leaving the old one stored. */
  startOver: () => void;
}

export function useTopicMapDraft(): TopicMapDraft {
  const [id, setId] = useState(storedDraftId);
  const [snapshot, setSnapshot] = useState<TopicMapSnapshot | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const session = useMemo(() => createDraftSession({ client, machine: topicMap, id }), [id]);
  const live = useRef(true);

  // Every read and step of the draft is numbered when it is sent, and an answer is applied only
  // when nothing sent after it has answered first, so a slow poll cannot put back the draft a step
  // the reader just took has replaced.
  const sent = useRef(0);
  const applied = useRef(0);
  const take = useCallback((request: number, json: string | null) => {
    if (json == null || request < applied.current) return;
    const read = topicMap.rehydrate(json);
    if (read.result !== "ok") return;
    applied.current = request;
    setSnapshot(read.snapshot);
  }, []);

  // Load the draft, or save the wizard's first step when there is none yet.
  useEffect(() => {
    live.current = true;
    void (async () => {
      const loading = ++sent.current;
      const loaded = await session.load();
      if (!live.current) return;
      if (!loaded.ok) return setProblem(loaded.message);
      if (loaded.snapshot) return take(loading, loaded.snapshot);
      const saving = ++sent.current;
      const saved = await session.save(topicMap.serialize(topicMap.initial()));
      if (live.current) (saved.ok ? take(saving, saved.snapshot) : setProblem(saved.message));
    })();
    return () => {
      live.current = false;
    };
  }, [session, take]);

  // While the map builds, only the run's outcome moves the draft on: poll the stored draft, one
  // read at a time, a second after the last one answered.
  const building = snapshot?.state === "Building";
  useEffect(() => {
    if (!building) return;
    let stopped = false;
    let timer: ReturnType<typeof setTimeout>;
    const poll = async () => {
      const request = ++sent.current;
      try {
        const loaded = await session.load();
        if (!stopped && loaded.ok) take(request, loaded.snapshot);
      } catch {
        // The host may be restarting; the next poll tries again.
      }
      if (!stopped) timer = setTimeout(poll, 1000);
    };
    timer = setTimeout(poll, 1000);
    return () => {
      stopped = true;
      clearTimeout(timer);
    };
  }, [building, session, take]);

  const can = useCallback(
    (trigger: TopicMapTrigger, input?: unknown) =>
      snapshot != null && topicMap.core.canFire(snapshot, trigger, input as never),
    [snapshot],
  );

  const advance = useCallback(
    async <T extends TopicMapTrigger>(trigger: T, input?: TopicMapSpec["triggers"][T]) => {
      if (!snapshot) return;
      // The twin computes the same step the server will; the server refuses a result that differs.
      const local = topicMap.core.advance(snapshot, trigger, input);
      setBusy(true);
      setProblem(null);
      const request = ++sent.current;
      const result = await session.advance(trigger, {
        input: input === undefined ? undefined : JSON.stringify(input),
        requestId: crypto.randomUUID(),
        clientResult: local.outcome === "transitioned" ? topicMap.serialize(local.snapshot as TopicMapSnapshot) : undefined,
      });
      setBusy(false);
      if (result.ok) take(request, result.snapshot);
      else setProblem(`${result.code}: ${result.message}`);
    },
    [snapshot, session, take],
  );

  const startOver = useCallback(() => {
    const fresh = crypto.randomUUID();
    try {
      localStorage.setItem(DRAFT_KEY, fresh);
    } catch {
      // A private window keeps no draft between visits; this one still works.
    }
    setSnapshot(null);
    setProblem(null);
    setId(fresh);
  }, []);

  return { id, snapshot, problem, busy, advance, can, startOver };
}
