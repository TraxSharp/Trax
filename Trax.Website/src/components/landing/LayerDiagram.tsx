import Link from "next/link";

const layers = [
  {
    rung: "Write it",
    name: "Core",
    cmd: "dotnet add package Trax.Core",
    desc: "Typed pipelines of junctions. A failing step skips the rest and the train arrives carrying the exception. No database and no DI container, just the pipeline.",
    replaces: "if-checks between service calls",
    href: "/docs/core",
  },
  {
    rung: "Record it",
    name: "Effect",
    cmd: "dotnet add package Trax.Effect",
    desc: "Every run gets a row: when it started, how it ended, which junction failed and why. Stored in PostgreSQL, SQLite or memory, and wired into .NET dependency injection.",
    replaces: "hand-rolled run logging",
    href: "/docs/effect",
  },
  {
    rung: "Call it",
    name: "Mediator",
    cmd: "dotnet add package Trax.Mediator",
    desc: "Run a train by handing over its input. The host checks every registered chain at startup and refuses to start if a junction's input can never be provided.",
    replaces: "MediatR",
    href: "/docs/mediator",
  },
  {
    rung: "Schedule it",
    name: "Scheduler",
    cmd: "dotnet add package Trax.Scheduler",
    desc: "Cron, intervals and one-off delays. Retries with backoff, dead letters, trains that wait on other trains, and workers on other machines or in Lambda.",
    replaces: "Hangfire, Quartz.NET",
    href: "/docs/scheduler",
  },
  {
    rung: "Serve it",
    name: "API",
    cmd: "dotnet add package Trax.Api.GraphQL",
    desc: "Typed GraphQL queries and mutations generated from your trains, with per-train authorization and lifecycle subscriptions. A train that does not say who may call it stops the host from starting.",
    replaces: "controllers written by hand",
    href: "/docs/api",
  },
  {
    rung: "Watch it",
    name: "Dashboard",
    cmd: "dotnet add package Trax.Dashboard",
    desc: "Mounts into your ASP.NET app. Every run, schedule and dead letter in one place, with requeue and run-with-new-input from the browser.",
    replaces: "a dashboard per tool",
    href: "/docs/dashboard",
  },
];

export default function LayerDiagram() {
  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">
          Take the layers you need
        </h2>
        <p className="mt-3 max-w-2xl text-text-secondary">
          Each package adds one thing on top of the one before it. Stop at
          whatever layer solves your problem, and add the next one when you
          need it. The trains you already wrote do not change.
        </p>

        {/* Vertical track with stops */}
        <div className="mt-12 space-y-0">
          {layers.map((layer, i) => (
            <div key={layer.name} className="relative flex">
              {/* Track line */}
              <div className="flex flex-col items-center mr-6">
                <div
                  className={`h-3 w-3 rounded-full border-2 ${
                    i === 0
                      ? "border-accent bg-accent"
                      : "border-border-hover bg-bg-primary"
                  }`}
                />
                {i < layers.length - 1 && (
                  <div className="w-px flex-1 bg-border" />
                )}
              </div>

              {/* Content */}
              <Link
                href={layer.href}
                className="group -mt-1 flex-1 pb-10"
              >
                <div className="flex flex-col gap-1 sm:flex-row sm:items-baseline sm:gap-3">
                  <span className="text-lg font-semibold text-text-primary group-hover:text-accent">
                    {layer.rung}
                  </span>
                  <span className="text-sm text-text-muted">{layer.name}</span>
                  <code className="text-xs text-text-muted">
                    {layer.cmd}
                  </code>
                </div>
                <p className="mt-2 max-w-xl text-sm leading-relaxed text-text-secondary">
                  {layer.desc}
                </p>
                <p className="mt-1 text-xs text-text-muted">
                  In place of {layer.replaces}
                </p>
              </Link>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
