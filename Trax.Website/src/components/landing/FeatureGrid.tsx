const features = [
  {
    title: "Compile-time chain validation",
    description:
      "A Roslyn analyzer checks your chains at build time. Mismatched types between junctions? You\u2019ll know before you run anything.",
    packages: ["Trax.Core"],
  },
  {
    title: "Execution metadata",
    description:
      "Every pipeline run is tracked \u2014 start time, end time, which junction failed, the exception, the input parameters. Queryable in Postgres or in-memory.",
    packages: ["Trax.Effect", "Trax.Effect.Data.Postgres"],
  },
  {
    title: "Job scheduling with dependencies",
    description:
      "Cron and interval schedules. Dependent job chains (DAGs). Automatic retries with backoff. Dead-letter queues for jobs that give up.",
    packages: ["Trax.Scheduler"],
  },
  {
    title: "CancellationToken everywhere",
    description:
      "First-class cancellation through pipelines, junctions, the dashboard, and the mediator. Not bolted on \u2014 it\u2019s threaded through the whole stack.",
    packages: ["Trax.Core", "Trax.Effect", "Trax.Mediator", "Trax.Dashboard"],
  },
  {
    title: "Auto-generated GraphQL API",
    description:
      "Tag a pipeline with [TraxMutation] and it gets a typed GraphQL mutation. [TraxQuery] makes a query. Real-time lifecycle events via WebSocket subscriptions.",
    packages: ["Trax.Api.GraphQL"],
  },
  {
    title: "Monitoring dashboard",
    description:
      "A Blazor Server UI that mounts into your existing app. View executions, inspect failures, requeue jobs, or run pipelines with entirely new inputs.",
    packages: ["Trax.Dashboard"],
  },
];

export default function FeatureGrid() {
  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">
          What you get.
        </h2>

        {/* Two-column flowing list, not cards */}
        <div className="mt-10 grid gap-x-16 gap-y-10 sm:grid-cols-2">
          {features.map((feature) => (
            <div key={feature.title}>
              <h3 className="text-sm font-semibold text-accent">
                {feature.title}
              </h3>
              <p className="mt-2 text-sm leading-relaxed text-text-secondary">
                {feature.description}
              </p>
              <div className="mt-2 flex flex-wrap gap-2">
                {feature.packages.map((pkg) => (
                  <a
                    key={pkg}
                    href={`https://www.nuget.org/packages/${pkg}/`}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="font-mono text-[11px] text-text-muted transition-colors hover:text-accent"
                  >
                    {pkg}
                  </a>
                ))}
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
