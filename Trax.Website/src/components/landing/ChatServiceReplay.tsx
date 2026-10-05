"use client";

import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { useFitCode } from "./useFitCode";
import data from "@/data/chat-service-recordings.json";
import {
  addCharlie,
  advance,
  begin,
  busy,
  choices,
  choose,
  describe,
  focusOf,
  lineOf,
  refuse,
  viewOf,
  type ChatPlayback,
  type ChatRecording,
  type Described,
  type Played,
  type ViewLine,
} from "@/lib/chat-service-replay";

// Recorded from the ChatService sample by Trax.Samples' scripts/recordings/chat-service.mjs, which writes this file
// with its --copy-to option. Re-record when the sample's trains, hook, subscription or client documents change.
const recording = data as ChatRecording;
const fileName = (path: string) => path.split("/").pop() ?? path;

export default function ChatServiceReplay() {
  const [playback, setPlayback] = useState<ChatPlayback | null>(null);
  const playing = playback != null && busy(playback);

  useEffect(() => {
    if (!playing) return;
    const timer = setInterval(() => setPlayback((p) => (p ? advance(p, performance.now()) : p)), 60);
    return () => clearInterval(timer);
  }, [playing]);

  const start = useCallback(() => setPlayback(begin(recording, performance.now())), []);
  const send = (key: string) => setPlayback((p) => (p ? (choose(recording, p, key, performance.now()) ?? p) : p));
  const letCharlieTry = () => setPlayback((p) => (p ? (refuse(recording, p, performance.now()) ?? p) : p));
  const letCharlieIn = () => setPlayback((p) => (p ? (addCharlie(recording, p, performance.now()) ?? p) : p));

  // Open the room the first time the demo is on screen, unless the reader prefers less motion.
  const root = useRef<HTMLDivElement>(null);
  const started = useRef(false);
  useEffect(() => {
    const node = root.current;
    if (!node || typeof IntersectionObserver === "undefined") return;
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
    const observer = new IntersectionObserver(
      ([entry]) => {
        if (!entry.isIntersecting || started.current) return;
        started.current = true;
        observer.disconnect();
        start();
      },
      { threshold: 0.2 },
    );
    observer.observe(node);
    return () => observer.disconnect();
  }, [start]);

  const offered = playback ? choices(recording, playback) : [];
  const prologueDone = playback != null && !playback.queue.some((q) => q.segment === "prologue");
  const charlieIn = playback?.charlieJoinedAt != null;

  return (
    <div ref={root} className="grid gap-4 lg:grid-cols-[230px_repeat(3,minmax(0,1fr))]">
      {/* Controls */}
      <div className="flex flex-col gap-5 rounded-lg border border-border bg-bg-secondary p-4 text-sm">
        <Group label="Next line">
          {!playback ? (
            <button
              onClick={start}
              className="rounded-md bg-accent px-3 py-2 text-sm font-medium text-white transition-colors hover:bg-accent-hover"
            >
              Open the room
            </button>
          ) : offered.length > 0 ? (
            <>
              <p className="text-xs text-text-muted">{offered[0].speaker} says:</p>
              {offered.map((line) => (
                <ActionButton key={line.key} onClick={() => send(line.key)}>
                  {line.content}
                </ActionButton>
              ))}
            </>
          ) : (
            <p className="min-h-[2.5em] text-xs leading-relaxed text-text-muted">
              {!prologueDone
                ? "Alice is creating the room and adding Bob..."
                : playing
                  ? "Sending..."
                  : "That is the whole conversation. Reset to take another path."}
            </p>
          )}
        </Group>

        <Group label="Charlie">
          <ActionButton onClick={letCharlieTry} disabled={!prologueDone || playing || playback?.refused || charlieIn}>
            Charlie tries to listen in
          </ActionButton>
          <p className="text-xs leading-relaxed text-text-muted">
            He has a key but is not in the room. Then a socket with no key at all tries.
          </p>
          <ActionButton onClick={letCharlieIn} disabled={!prologueDone || playing || charlieIn}>
            Alice adds Charlie
          </ActionButton>
          <p className="text-xs leading-relaxed text-text-muted">
            His client loads the history so far and subscribes, and hears every line after it.
          </p>
        </Group>

        <ActionButton onClick={() => setPlayback(null)} disabled={!playback || playing}>
          Reset
        </ActionButton>
      </div>

      <ChatView user="Alice" playback={playback} />
      <ChatView user="Bob" playback={playback} />
      <ChatView user="Charlie" playback={playback} />

      <div className="grid gap-4 lg:col-span-4 lg:grid-cols-[minmax(0,5fr)_minmax(0,7fr)]">
        <UnderTheHood played={playback?.played ?? []} />
        <CodePanel played={playback?.played ?? []} />
      </div>

      <p className="text-xs leading-relaxed text-text-muted lg:col-span-4">
        Recorded from the ChatService sample signed in as Alice, Bob and Charlie with its demo keys, and played back in
        your browser. Every request, response and WebSocket frame above is one the host sent or received; the keys are
        masked, as the sample&apos;s own panel masks them. The lines on offer were each sent to the room once, and
        the conversation is the path you pick through them. Charlie&apos;s join was recorded at every point of the
        conversation, so the history he loads is the one you picked.
      </p>
    </div>
  );
}

function Group({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-2.5">
      <span className="text-[11px] font-semibold uppercase tracking-wider text-text-muted">{label}</span>
      {children}
    </div>
  );
}

function ActionButton({ children, ...props }: React.ButtonHTMLAttributes<HTMLButtonElement>) {
  return (
    <button
      {...props}
      className="rounded-md border border-border bg-bg-tertiary px-3 py-2 text-left text-sm text-text-primary transition-colors hover:border-accent/60 disabled:cursor-default disabled:opacity-35 disabled:hover:border-border"
    >
      {children}
    </button>
  );
}

function PanelTitle({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex items-center gap-3 border-b border-border px-4 py-2 text-[11px] font-semibold uppercase tracking-wider text-text-muted">
      {children}
    </div>
  );
}

/** Scrolls a box to its end when `count` changes, without moving the page. */
function useStickToEnd(count: number) {
  const box = useRef<HTMLDivElement>(null);
  useLayoutEffect(() => {
    if (box.current) box.current.scrollTop = box.current.scrollHeight;
  }, [count]);
  return box;
}

function ChatView({ user, playback }: { user: string; playback: ChatPlayback | null }) {
  const view = playback ? viewOf(playback, user) : { room: false, subscribed: false, lines: [] as ViewLine[] };
  const box = useStickToEnd(view.lines.length);
  return (
    <div className="flex min-w-0 flex-col overflow-hidden rounded-lg border border-border bg-bg-secondary">
      <PanelTitle>
        <span className="normal-case tracking-normal text-text-primary">{user}</span>
        {view.room && <span className="font-mono normal-case tracking-normal">#{recording.roomName}</span>}
        {view.subscribed && (
          <span className="ml-auto rounded-full border border-accent/40 px-2 py-0.5 normal-case tracking-normal text-accent-bright">
            subscribed
          </span>
        )}
      </PanelTitle>
      <div ref={box} className="flex h-[280px] flex-col gap-2 overflow-y-auto px-4 py-3">
        {view.lines.length === 0 && (
          <p className="text-sm text-text-muted">
            {view.subscribed ? "Listening on onChatEvent." : view.room ? "Room created." : "Not in the room."}
          </p>
        )}
        {view.lines.map((line) =>
          line.kind === "system" ? (
            <p key={line.key} className="text-center text-xs text-text-muted">
              {line.content}
            </p>
          ) : (
            <div key={line.key} className={`flex flex-col ${line.mine ? "items-end" : "items-start"}`}>
              <span className="mb-0.5 text-[11px] text-text-muted">
                {line.sender}
                {line.sentAt ? ` · ${line.sentAt.slice(11, 19)} UTC` : ""}
              </span>
              <span
                className={`max-w-[85%] rounded-lg px-3 py-1.5 text-sm ${
                  line.mine ? "bg-accent text-white" : "bg-bg-tertiary text-text-primary"
                }`}
              >
                {line.content}
              </span>
            </div>
          ),
        )}
      </div>
    </div>
  );
}

const TONES: Record<Described["tone"], string> = {
  out: "text-info",
  in: "text-accent-bright",
  event: "text-signal",
  refused: "text-derail",
  info: "text-text-muted",
};
const ARROWS: Record<string, string> = { out: "→", in: "←", info: "•" };

function UnderTheHood({ played }: { played: Played[] }) {
  const [open, setOpen] = useState<number | null>(null);
  const box = useStickToEnd(played.length);
  return (
    <div className="flex min-w-0 flex-col overflow-hidden rounded-lg border border-border bg-bg-secondary">
      <PanelTitle>Under the hood</PanelTitle>
      <div ref={box} className="h-[380px] overflow-y-auto px-2 py-2">
        {played.length === 0 && (
          <p className="px-2 py-1 text-sm text-text-muted">
            Every GraphQL request and response, and every WebSocket frame, appears here as it happens.
          </p>
        )}
        {played.map((entry) => {
          const d = describe(entry, played);
          const expanded = open === entry.id;
          return (
            <div key={entry.id} className="rounded px-2 py-1.5 hover:bg-bg-tertiary/60">
              <button
                onClick={() => setOpen(expanded ? null : entry.id)}
                className="flex w-full flex-wrap items-baseline gap-x-2 text-left"
                aria-expanded={expanded}
              >
                <span className={`font-mono text-xs ${TONES[d.tone]}`}>{ARROWS[entry.direction]}</span>
                <span className="rounded border border-border px-1 font-mono text-[10px] uppercase text-text-muted">
                  {entry.channel}
                </span>
                <span className="text-xs text-text-secondary">{entry.user}</span>
                <span className={`font-mono text-xs ${TONES[d.tone]}`}>{d.title}</span>
                {entry.durationMs != null && <span className="text-[11px] text-text-muted">{entry.durationMs} ms</span>}
              </button>
              {d.note && <p className="ml-5 mt-0.5 text-xs leading-relaxed text-text-muted">{d.note}</p>}
              {expanded && entry.detail != null && (
                <pre className="ml-5 mt-1 max-h-60 overflow-auto rounded border border-border bg-bg-primary p-2 font-mono text-[11px] leading-relaxed text-text-secondary">
                  {JSON.stringify(entry.detail, null, 2)}
                </pre>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}

// The words that carry the feature, picked out in the code.
const TOKEN =
  /(\b(?:SendAsync|SubscribeAsync|Topic|Chain|TraxAuthorize|TraxMutation|Subscribe|ExtendObjectType)\b)|("[^"]*")|(\b(?:public|class|protected|override|async|await|return|throw|new|if)\b)/g;

function highlight(line: string) {
  const parts: React.ReactNode[] = [];
  let last = 0;
  for (const m of line.matchAll(TOKEN)) {
    if (m.index > last) parts.push(line.slice(last, m.index));
    const tone = m[1] ? "text-signal" : m[2] ? "text-accent-bright" : "text-text-muted";
    parts.push(
      <span key={m.index} className={tone}>
        {m[0]}
      </span>,
    );
    last = m.index + m[0].length;
  }
  parts.push(line.slice(last));
  return parts;
}

function latestFocus(played: Played[]): { file: string; needle: string; id: number } | null {
  for (let i = played.length - 1; i >= 0; i--) {
    const f = focusOf(played[i]);
    if (f) return { ...f, id: played[i].id };
  }
  return null;
}

function CodePanel({ played }: { played: Played[] }) {
  // The latest entry that exercised one of the files: the panel follows it, until the reader picks a file.
  const focus = latestFocus(played);
  const [picked, setPicked] = useState<{ file: string; after: number } | null>(null);
  const followed = picked && (focus?.id ?? -1) <= picked.after ? picked.file : (focus?.file ?? fileName(recording.sources[0].path));
  const source = recording.sources.find((s) => fileName(s.path) === followed) ?? recording.sources[0];

  const lines = useMemo(() => source.code.split("\n"), [source.code]);
  // From the class's attributes or declaration down, past the usings and the doc comment.
  const first = Math.max(
    lines.findIndex((l) => /^(\[|public )/.test(l)),
    0,
  );
  let last = lines.length;
  while (last > 0 && lines[last - 1].trim() === "") last--;
  const highlighted = focus && focus.file === fileName(source.path) ? lineOf(source.code, focus.needle) : -1;

  // Bring the highlighted line into view inside the panel, never by scrolling the page.
  const pane = useRef<HTMLPreElement>(null);
  useFitCode(pane, source.path, 11.5);
  useLayoutEffect(() => {
    const p = pane.current;
    const row = p?.querySelector<HTMLElement>("[data-highlighted]");
    if (!p || !row) return;
    const top = row.offsetTop - p.offsetTop;
    if (top < p.scrollTop || top > p.scrollTop + p.clientHeight - 40) p.scrollTop = Math.max(0, top - p.clientHeight / 3);
  }, [highlighted, source.path]);

  return (
    <div className="flex min-w-0 flex-col overflow-hidden rounded-lg border border-border bg-bg-secondary">
      <div className="flex flex-wrap gap-x-1 border-b border-border px-2 pt-1.5">
        {recording.sources.map((s) => {
          const name = fileName(s.path);
          return (
            <button
              key={s.path}
              onClick={() => setPicked({ file: name, after: focus?.id ?? -1 })}
              className={`-mb-px border-b-2 px-2 py-1.5 font-mono text-[11px] transition-colors ${
                name === fileName(source.path)
                  ? "border-accent text-text-primary"
                  : "border-transparent text-text-muted hover:text-text-secondary"
              }`}
            >
              {name}
            </button>
          );
        })}
      </div>
      <pre ref={pane} className="relative h-[340px] overflow-auto py-3 font-mono text-[11.5px] leading-[1.65] text-text-secondary">
        {lines.slice(first, last).map((line, offset) => {
          const i = first + offset;
          return (
            <div
              key={`${source.path}:${i}`}
              data-highlighted={i === highlighted ? "" : undefined}
              className={`flex w-max min-w-full border-l-2 pr-4 transition-colors duration-200 ${
                i === highlighted ? "border-signal bg-signal/10" : "border-transparent"
              }`}
            >
              <span className="mr-3 w-8 shrink-0 select-none text-right text-text-muted/60">{i + 1}</span>
              <span className="whitespace-pre">{highlight(line)}</span>
            </div>
          );
        })}
      </pre>
    </div>
  );
}
