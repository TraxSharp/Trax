"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import BoardReplay from "./BoardReplay";
import ChatServiceReplay from "./ChatServiceReplay";
import OrderReplay from "./OrderReplay";
import PersistedOperationsReplay from "./PersistedOperationsReplay";
import RecoveryReplay from "./RecoveryReplay";
import SampleSource, { type SampleSourceProps } from "./SampleSource";
import boardData from "@/data/board-recordings.json";
import type { BoardRecordings } from "@/lib/board-replay";

const boardRecordings = boardData as unknown as BoardRecordings;

// Each tab plays runs of the real sample, recorded by Trax.Samples' scripts/recordings and copied into src/data with
// its --copy-to option. Re-record a sample when its code or its page changes.
interface Sample {
  tab: string;
  /** One line under the tab's name, saying what the demo shows. */
  blurb: string;
  href: string;
  source: SampleSourceProps;
}

const samples: Sample[] = [
  {
    tab: "Agents",
    blurb: "Scripted agents work a ticket board. The server runs every check itself.",
    href: "/docs/samples/board",
    source: {
      folder: "Board",
      files: [
        { path: "Trax.Samples.Board/Machines/AssignmentMachine.cs", label: "an agent's assignment: its events and the checks they run" },
        { path: "Trax.Samples.Board/Machines/TicketMachine.cs", label: "the board: lanes only the server moves" },
        { path: "Trax.Samples.Board/Trains/Checks/ReproduceTrain.cs", label: "a check: the server runs the agent's case itself" },
        { path: "Trax.Samples.Board.Agents/src/scenarios.ts", label: "the scripted agents, on the generated client" },
      ],
      docs: "/docs/samples/board",
      run: [
        "docker compose up -d database",
        "dotnet run --project samples/Board/Trax.Samples.Board.Api",
        "cd samples/Board/Trax.Samples.Board.Agents && npm ci && npm run scenario",
      ],
    },
  },
  {
    tab: "State Machines",
    blurb: "An order moves through states the server enforces, with a delivery timer.",
    href: "/docs/samples/recovery",
    source: {
      folder: "Recovery",
      files: [
        { path: "Trax.Samples.Recovery/Machines/OrderMachine.cs", label: "the order: its states, events and timer" },
        { path: "Trax.Samples.Recovery/Trains/Order/DeliverOrderTrain.cs", label: "the delivery the order runs" },
        { path: "Trax.Samples.Recovery.Api/Program.cs", label: "the host: machines, junction events and pushes" },
      ],
      docs: "/docs/samples/recovery",
      run: [
        "docker compose up -d database",
        "dotnet run --project samples/Recovery/Trax.Samples.Recovery.Api",
        "cd samples/Recovery/Trax.Samples.Recovery.Client && npm ci && npm run dev",
      ],
    },
  },
  {
    tab: "Workflows",
    blurb: "A refund asks a model two questions and resumes from a checkpoint.",
    href: "/docs/samples/recovery",
    source: {
      folder: "Recovery",
      files: [
        { path: "Trax.Samples.Recovery/Trains/Refund/ApproveRefundTrain.cs", label: "the refund: two questions and a checkpoint" },
        { path: "Trax.Samples.Recovery/Trains/Refund/RefundModel.cs", label: "the questions the model is asked" },
        { path: "Trax.Samples.Recovery/Model/DemoDecider.cs", label: "the stand-in decision model" },
        { path: "Trax.Samples.Recovery.Api/Program.cs", label: "the host: decision recording, junction events, the scheduler" },
      ],
      docs: "/docs/samples/recovery",
      run: [
        "docker compose up -d database",
        "dotnet run --project samples/Recovery/Trax.Samples.Recovery.Api",
        "cd samples/Recovery/Trax.Samples.Recovery.Client && npm ci && npm run dev",
      ],
    },
  },
  {
    tab: "Live Updates",
    blurb: "Each chat message runs a train and is pushed to the room.",
    href: "/docs/samples/chat-service",
    source: {
      folder: "ChatService",
      files: [
        { path: "Trax.Samples.ChatService/Trains/SendMessage/SendMessageTrain.cs", label: "the train each message runs" },
        { path: "Trax.Samples.ChatService/Hooks/ChatLifecycleHook.cs", label: "publishes a completed train's output to the room's topic" },
        { path: "Trax.Samples.ChatService/Subscriptions/ChatSubscriptions.cs", label: "onChatEvent, admitting only the room's participants" },
        { path: "Trax.Samples.ChatService.Api/Program.cs", label: "API keys, the hook and the subscription wired up" },
      ],
      docs: "/docs/samples/chat-service",
      run: [
        "dotnet run --project samples/ChatService/Trax.Samples.ChatService.Api",
        "cd samples/ChatService/Trax.Samples.ChatService.Client && npm ci && npm run dev",
      ],
    },
  },
  {
    tab: "Persisted Operations",
    blurb: "Only stored queries run, and only the operator can edit them.",
    href: "/docs/samples/persisted-operations",
    source: {
      folder: "PersistedOperations",
      files: [
        { path: "Trax.Samples.PersistedOperations.Api/Program.cs", label: "the host: only stored queries run, and only the operator edits them" },
        { path: "Trax.Samples.PersistedOperations/Trains/Greeting/Greet/Junctions/ComposeGreetingJunction.cs", label: "the casual and formal greetings" },
        { path: "Trax.Samples.PersistedOperations.Api/wwwroot/demo.js", label: "the page: the three stories, as requests" },
        { path: "Trax.Samples.PersistedOperations.Client/manifest.json", label: "the ids and their documents" },
      ],
      docs: "/docs/samples/persisted-operations",
      run: [
        "docker compose up -d",
        "dotnet run --project samples/PersistedOperations/Trax.Samples.PersistedOperations.Api",
        "open http://localhost:5240",
      ],
    },
  },
];

export default function BuiltWithTrax() {
  const [active, setActive] = useState(0);

  // The demos differ in height, and so does one demo from moment to moment. The panel only ever grows to the
  // tallest it has been, so switching to a shorter tab or a shorter moment never pulls the page up under the
  // reader. A change of width starts over, since every height changes with it.
  const panel = useRef<HTMLDivElement>(null);
  const [minHeight, setMinHeight] = useState(0);
  useEffect(() => {
    const node = panel.current;
    if (!node) return;
    let width = window.innerWidth;
    const observer = new ResizeObserver(() => setMinHeight((m) => Math.max(m, node.scrollHeight)));
    const onResize = () => {
      if (window.innerWidth === width) return;
      width = window.innerWidth;
      setMinHeight(0);
    };
    observer.observe(node.firstElementChild ?? node);
    window.addEventListener("resize", onResize);
    return () => {
      observer.disconnect();
      window.removeEventListener("resize", onResize);
    };
  }, []);
  const sample = samples[active];
  const next = samples[(active + 1) % samples.length];

  const section = useRef<HTMLElement>(null);
  const tabs = useRef<(HTMLButtonElement | null)[]>([]);
  const go = (step: number) => {
    const i = (active + step + samples.length) % samples.length;
    setActive(i);
    if (tabs.current.some((t) => t === document.activeElement)) tabs.current[i]?.focus();
  };

  return (
    <section ref={section} className="scroll-mt-20 border-b border-border pb-24 pt-8">
      {/* Wider than the rest of the page, so a demo fits on one screen without scrolling between its steps. */}
      <div className="mx-auto max-w-[1280px] px-4 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">Built with Trax</h2>
        <p className="mt-3 max-w-2xl text-text-secondary">
          Five sample apps written on Trax. Each demo replays a run recorded from the real app, with the app&apos;s own
          code underneath.
        </p>

        {/* Cards rather than underlined tabs: readers took the old tab row for a heading and never switched. */}
        <div
          role="tablist"
          aria-label="Sample apps"
          className="mt-8 grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-5"
          onKeyDown={(e) => {
            if (e.key === "ArrowRight") go(1);
            else if (e.key === "ArrowLeft") go(-1);
            else return;
            e.preventDefault();
          }}
        >
          {samples.map((s, i) => (
            <button
              key={s.tab}
              ref={(node) => {
                tabs.current[i] = node;
              }}
              role="tab"
              aria-selected={i === active}
              tabIndex={i === active ? 0 : -1}
              onClick={() => setActive(i)}
              className={`group flex flex-col rounded-lg border p-4 text-left transition-colors ${
                i === active
                  ? "border-accent bg-accent-muted"
                  : "cursor-pointer border-border bg-bg-secondary hover:border-border-hover hover:bg-bg-hover"
              }`}
            >
              <span className="flex items-center justify-between gap-2">
                <span className={`text-sm font-semibold ${i === active ? "text-text-primary" : "text-text-secondary group-hover:text-text-primary"}`}>
                  {s.tab}
                </span>
                <span className={`font-mono text-[11px] ${i === active ? "text-accent" : "text-text-muted"}`}>
                  {i + 1}/{samples.length}
                </span>
              </span>
              <span className="mt-1.5 text-xs leading-snug text-text-muted">{s.blurb}</span>
            </button>
          ))}
        </div>

        <div className="mt-6 flex flex-wrap items-center gap-x-5 gap-y-2 text-sm">
          <div className="flex items-center gap-1">
            <button
              type="button"
              onClick={() => go(-1)}
              aria-label={`Previous sample: ${samples[(active + samples.length - 1) % samples.length].tab}`}
              className="rounded border border-border px-2 py-0.5 text-text-secondary hover:border-border-hover hover:text-text-primary"
            >
              &lsaquo;
            </button>
            <span className="min-w-[3.5rem] text-center font-mono text-xs text-text-muted">
              {active + 1} / {samples.length}
            </span>
            <button
              type="button"
              onClick={() => go(1)}
              aria-label={`Next sample: ${next.tab}`}
              className="rounded border border-border px-2 py-0.5 text-text-secondary hover:border-border-hover hover:text-text-primary"
            >
              &rsaquo;
            </button>
          </div>
          <span className="font-medium text-text-primary">{sample.tab}</span>
          <div className="ml-auto flex items-center gap-x-5">
            <Link href={sample.href} className="font-medium text-accent hover:text-accent-hover">
              Read the sample &rarr;
            </Link>
            <Link href="/docs/samples" className="text-text-muted hover:text-text-secondary">
              All samples &rarr;
            </Link>
          </div>
        </div>

        <div role="tabpanel" ref={panel} className="pt-4" style={minHeight ? { minHeight } : undefined}>
          <div>
            {sample.tab === "Agents" ? (
              <BoardReplay recordings={boardRecordings} />
            ) : sample.tab === "State Machines" ? (
              <OrderReplay />
            ) : sample.tab === "Workflows" ? (
              <RecoveryReplay />
            ) : sample.tab === "Live Updates" ? (
              <ChatServiceReplay />
            ) : (
              <PersistedOperationsReplay />
            )}
            <SampleSource {...sample.source} />
            {/* Someone who watched a demo to the end is below the cards, so the next one is offered here too. */}
            <button
              type="button"
              onClick={() => {
                go(1);
                section.current?.scrollIntoView({ behavior: "smooth", block: "start" });
              }}
              className="mt-4 flex w-full items-center justify-between gap-4 rounded-lg border border-border bg-bg-secondary px-4 py-3 text-left hover:border-accent hover:bg-bg-hover"
            >
              <span>
                <span className="block text-[11px] font-semibold uppercase tracking-wider text-text-muted">
                  Next sample, {active + 2 > samples.length ? 1 : active + 2} of {samples.length}
                </span>
                <span className="mt-0.5 block text-sm font-semibold text-text-primary">{next.tab}</span>
                <span className="block text-xs text-text-muted">{next.blurb}</span>
              </span>
              <span className="text-lg text-accent">&rarr;</span>
            </button>
          </div>
        </div>
      </div>
    </section>
  );
}
