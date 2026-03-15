import Link from "next/link";

const layers = [
  {
    name: "Core",
    cmd: "dotnet add package Trax.Core",
    desc: "The pipeline engine. Define sequences of steps with typed inputs and outputs. Errors short-circuit automatically. No dependencies, no database, no DI container — just the pipeline.",
    href: "/docs/core",
  },
  {
    name: "Effect",
    cmd: "dotnet add package Trax.Effect",
    desc: "Adds execution tracking. Every pipeline run gets recorded — when it started, when it finished, which step failed, what the input was. Stores to PostgreSQL or in-memory. Plugs into .NET dependency injection.",
    href: "/docs/effect",
  },
  {
    name: "Mediator",
    cmd: "dotnet add package Trax.Mediator",
    desc: "Decouples the caller from the pipeline. Instead of calling a specific pipeline directly, you hand off an input and the mediator routes it to the right one. Useful in larger apps where you don't want tight coupling between components.",
    href: "/docs/mediator",
  },
  {
    name: "Scheduler",
    cmd: "dotnet add package Trax.Scheduler",
    desc: "Run pipelines on a schedule — cron expressions, fixed intervals, or one-off delayed jobs. Automatic retries with backoff. Dead-letter handling for jobs that keep failing. Job dependencies so one pipeline waits for another to finish.",
    href: "/docs/scheduler",
  },
  {
    name: "API",
    cmd: "dotnet add package Trax.Api.GraphQL",
    desc: "Turns your pipelines into a GraphQL API automatically. Tag a pipeline as a query or mutation and HotChocolate picks it up — typed input, typed output, no controller code. Real-time WebSocket subscriptions for lifecycle events come free.",
    href: "/docs/api",
  },
  {
    name: "Dashboard",
    cmd: "dotnet add package Trax.Dashboard",
    desc: "A monitoring UI that mounts into your existing ASP.NET app. See every execution, inspect failures, requeue jobs, or run a pipeline with new inputs directly from the browser.",
    href: "/docs/dashboard",
  },
];

export default function LayerDiagram() {
  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">
          Pipelines are just the starting point.
        </h2>
        <p className="mt-3 max-w-2xl text-text-secondary">
          Trax is a stack of packages, each adding one capability on top of
          the last. You install only what you need — most projects start with
          Core and add layers over time as requirements grow.
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
                    {layer.name}
                  </span>
                  <code className="text-xs text-text-muted">
                    {layer.cmd}
                  </code>
                </div>
                <p className="mt-2 max-w-xl text-sm leading-relaxed text-text-secondary">
                  {layer.desc}
                </p>
              </Link>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
