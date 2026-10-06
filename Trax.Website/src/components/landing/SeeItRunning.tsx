"use client";

import Link from "next/link";
import { useState } from "react";
import ChatServiceReplay from "./ChatServiceReplay";
import PersistedOperationsReplay from "./PersistedOperationsReplay";
import RecoveryReplay from "./RecoveryReplay";
import StateMachineReplay from "./StateMachineReplay";
import SignalRReplay from "./SignalRReplay";
import SampleSource, { type SampleSourceProps } from "./SampleSource";

// Each tab plays runs of the real sample, recorded by Trax.Samples' scripts/recordings and copied into src/data with
// its --copy-to option. Re-record a sample when its code or its page changes.
interface Sample {
  tab: string;
  title: string;
  body: string;
  shows: string[];
  href: string;
  source: SampleSourceProps;
}

const samples: Sample[] = [
  {
    tab: "Recovery",
    title: "A run that recovers without asking the model again",
    body: "A train asks a decision model which track to take, a later step crashes, and the manifest's retry takes the same tracks by replaying the recorded answers. Every junction, question and track reaches the page as a junction event.",
    shows: ["Switch, Scale and Gate tracks", "Retries that replay recorded decisions", "Junction events over a GraphQL subscription"],
    href: "/docs/samples/recovery",
    source: {
      folder: "Recovery",
      files: [
        { path: "Trax.Samples.Recovery/Trains/Research/ResearchTopicTrain.cs", label: "the research train: a Switch and a Scale" },
        { path: "Trax.Samples.Recovery/Trains/Refund/ApproveRefundTrain.cs", label: "the refund train: a Gate" },
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
    tab: "State machine",
    title: "A side effect that runs exactly once",
    body: "Two state machines served over four generic GraphQL mutations. The server holds the state and runs the charge bound to the Pay edge itself, so pressing Pay twice leaves one charge with the payment provider.",
    shows: ["Machines authored with the fluent API", "The server decides what a valid draft is", "An effect bound to an edge, run exactly once"],
    href: "/docs/samples/state-machine",
    source: {
      folder: "StateMachine",
      files: [
        { path: "Trax.Samples.StateMachine/Machines.cs", label: "both machines, authored with the fluent API, and the charge" },
        { path: "Trax.Samples.StateMachine.Api/Program.cs", label: "the host: AddStateMachines in one line" },
        { path: "Trax.Samples.StateMachine/Payments.cs", label: "the simulated payment provider" },
        { path: "Trax.Samples.StateMachine/SnapshotPrincipal.cs", label: "maps the caller to the draft's user key" },
        { path: "web/src/traxTransport.ts", label: "the page's client for the four mutations" },
      ],
      docs: "/docs/samples/state-machine",
      run: [
        "docker compose up -d",
        "dotnet run --project samples/StateMachine/Trax.Samples.StateMachine.Api",
        "cd samples/StateMachine/web && npm install && npm run dev",
      ],
    },
  },
  {
    tab: "Chat over WebSockets",
    title: "Every message is a train",
    body: "Each chat mutation runs a train. When it completes, a lifecycle hook publishes the result to the room's topic, and every participant subscribed over a WebSocket receives it. A panel shows the traffic as it happens.",
    shows: ["GraphQL subscriptions fed by a lifecycle hook", "The API key in connection_init, per-room authorization", "The caller read from TraxPrincipal, never from the input"],
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
    tab: "SignalR",
    title: "Run events, pushed to the browser",
    body: "Started, Completed and Failed arrive over a SignalR hub that only signed-in operators may join, and a failure's reason is sent only when it was written to be read.",
    shows: ["UseSignalRHub plus MapTraxTrainEventHub", "A hub that will not start without an access posture", "Failure reasons withheld unless meant for the reader"],
    href: "/docs/samples/signalr-broadcaster",
    source: {
      folder: "SignalRBroadcaster",
      files: [
        { path: "Trax.Samples.SignalRBroadcaster/Program.cs", label: "the hub, the cookie sign-in and the SignalR sink" },
        { path: "Trax.Samples.SignalRBroadcaster/LiveTrainEvent.cs", label: "the projection that withholds reasons not meant to be read" },
        { path: "Trax.Samples.SignalRBroadcaster/Trains/Ping/Junctions/PingJunction.cs", label: "the junction that succeeds or fails on request" },
        { path: "Trax.Samples.SignalRBroadcaster/wwwroot/index.html", label: "the plain SignalR browser client" },
      ],
      docs: "/docs/samples/signalr-broadcaster",
      run: ["dotnet run --project samples/SignalRBroadcaster/Trax.Samples.SignalRBroadcaster", "open http://localhost:5270"],
    },
  },
  {
    tab: "Persisted operations",
    title: "A GraphQL API that runs only stored documents",
    body: "A client sends an operation id and its variables, and the server runs the document it stores for that id. An operator can hot-fix the stored document without shipping a new client, and an edit that would change the response shipped clients read is refused.",
    shows: ["RequirePersisted refuses every inline document", "Management mutations gated to one role", "The shape-diff guardrail on every edit"],
    href: "/docs/samples/persisted-operations",
    source: {
      folder: "PersistedOperations",
      files: [
        { path: "Trax.Samples.PersistedOperations.Api/Program.cs", label: "UsePersistedOperations, RequirePersisted, GateOperations" },
        { path: "Trax.Samples.PersistedOperations/Trains/Greeting/Greet/GreetTrain.cs", label: "the train greet_v1 calls" },
        { path: "Trax.Samples.PersistedOperations/Trains/Users/LookupUser/LookupUserTrain.cs", label: "the train lookupUser_v1 calls" },
        { path: "Trax.Samples.PersistedOperations.Client/Program.cs", label: "upload, call by id, hot-fix, the refused edit" },
        { path: "Trax.Samples.PersistedOperations.Client/manifest.json", label: "the ids and their documents" },
      ],
      docs: "/docs/samples/persisted-operations",
      run: [
        "docker compose up -d",
        "dotnet run --project samples/PersistedOperations/Trax.Samples.PersistedOperations.Api",
        "dotnet run --project samples/PersistedOperations/Trax.Samples.PersistedOperations.Client",
      ],
    },
  },
];

export default function SeeItRunning() {
  const [active, setActive] = useState(0);
  const sample = samples[active];

  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-7xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">See it running</h2>
        <p className="mt-3 max-w-2xl text-text-secondary">
          Each sample is a complete app you can run in a few minutes. Every tab plays recordings of the
          real sample in your browser, so you can try it here, then start the same C# yourself.
        </p>

        <div role="tablist" className="mt-10 flex flex-wrap gap-1 border-b border-border">
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

        <div role="tabpanel" className="pt-8">
          <div className="grid gap-x-10 gap-y-4 lg:grid-cols-[minmax(0,3fr)_minmax(0,2fr)]">
            <div>
              <h3 className="text-lg font-semibold text-text-primary">{sample.title}</h3>
              <p className="mt-2 text-sm leading-relaxed text-text-secondary">{sample.body}</p>
            </div>
            <div className="text-sm">
              <ul className="space-y-1.5 text-text-secondary">
                {sample.shows.map((s) => (
                  <li key={s} className="flex gap-2">
                    <span className="text-accent">&#8226;</span>
                    {s}
                  </li>
                ))}
              </ul>
              <div className="mt-4 flex flex-wrap items-center gap-x-5 gap-y-2">
                <Link href={sample.href} className="font-medium text-accent hover:text-accent-hover">
                  Read the sample &rarr;
                </Link>
                <Link href="/docs/samples" className="text-text-muted hover:text-text-secondary">
                  All samples &rarr;
                </Link>
              </div>
            </div>
          </div>

          <div className="mt-8">
            {sample.tab === "Recovery" ? (
              <RecoveryReplay />
            ) : sample.tab === "State machine" ? (
              <StateMachineReplay />
            ) : sample.tab === "Chat over WebSockets" ? (
              <ChatServiceReplay />
            ) : sample.tab === "SignalR" ? (
              <SignalRReplay />
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
