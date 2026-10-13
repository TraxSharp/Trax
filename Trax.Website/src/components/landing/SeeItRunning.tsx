"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import ChatServiceReplay from "./ChatServiceReplay";
import OrderReplay from "./OrderReplay";
import PersistedOperationsReplay from "./PersistedOperationsReplay";
import RecoveryReplay from "./RecoveryReplay";
import SampleSource, { type SampleSourceProps } from "./SampleSource";

// Each tab plays runs of the real sample, recorded by Trax.Samples' scripts/recordings and copied into src/data with
// its --copy-to option. Re-record a sample when its code or its page changes.
interface Sample {
  tab: string;
  href: string;
  source: SampleSourceProps;
}

const samples: Sample[] = [
  {
    tab: "State Machines",
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

export default function SeeItRunning() {
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

  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">See it running</h2>
        <p className="mt-3 max-w-2xl text-text-secondary">
          Small apps built with Trax. Each tab replays a real run of one in your browser.
        </p>

        <div className="mt-8 flex flex-wrap items-end gap-x-6 gap-y-3 border-b border-border">
          <div role="tablist" className="flex flex-wrap gap-1">
            {samples.map((s, i) => (
              <button
                key={s.tab}
                role="tab"
                aria-selected={i === active}
                onClick={() => setActive(i)}
                className={`-mb-px border-b-2 px-4 py-2 text-sm font-medium transition-colors ${
                  i === active
                    ? "border-accent text-text-primary"
                    : "border-transparent text-text-muted hover:text-text-secondary"
                }`}
              >
                {s.tab}
              </button>
            ))}
          </div>
          <div className="ml-auto flex items-center gap-x-5 pb-2 text-sm">
            <Link href={sample.href} className="font-medium text-accent hover:text-accent-hover">
              Read the sample &rarr;
            </Link>
            <Link href="/docs/samples" className="text-text-muted hover:text-text-secondary">
              All samples &rarr;
            </Link>
          </div>
        </div>

        <div role="tabpanel" ref={panel} className="pt-8" style={minHeight ? { minHeight } : undefined}>
          <div>
            {sample.tab === "State Machines" ? (
              <OrderReplay />
            ) : sample.tab === "Workflows" ? (
              <RecoveryReplay />
            ) : sample.tab === "Live Updates" ? (
              <ChatServiceReplay />
            ) : (
              <PersistedOperationsReplay />
            )}
            <SampleSource {...sample.source} />
          </div>
        </div>
      </div>
    </section>
  );
}
