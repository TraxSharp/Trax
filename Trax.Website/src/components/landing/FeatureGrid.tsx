export default function FeatureGrid() {
  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">
          What you get.
        </h2>

        {/* Two-column flowing list, not cards */}
        <div className="mt-10 grid gap-x-16 gap-y-10 sm:grid-cols-2">
          <div>
            <h3 className="text-sm font-semibold text-accent">
              Compile-time chain validation
            </h3>
            <p className="mt-2 text-sm leading-relaxed text-text-secondary">
              A Roslyn analyzer checks your chains at build time. Mismatched
              types between steps? You&apos;ll know before you run anything.
            </p>
          </div>

          <div>
            <h3 className="text-sm font-semibold text-accent">
              Execution metadata
            </h3>
            <p className="mt-2 text-sm leading-relaxed text-text-secondary">
              Every pipeline run is tracked — start time, end time, which step
              failed, the exception, the input parameters. Queryable in
              Postgres or in-memory.
            </p>
          </div>

          <div>
            <h3 className="text-sm font-semibold text-accent">
              Job scheduling with dependencies
            </h3>
            <p className="mt-2 text-sm leading-relaxed text-text-secondary">
              Cron and interval schedules. Dependent job chains (DAGs).
              Automatic retries with backoff. Dead-letter queues for jobs
              that give up.
            </p>
          </div>

          <div>
            <h3 className="text-sm font-semibold text-accent">
              CancellationToken everywhere
            </h3>
            <p className="mt-2 text-sm leading-relaxed text-text-secondary">
              First-class cancellation through pipelines, steps, the dashboard,
              and the mediator. Not bolted on — it&apos;s threaded through the
              whole stack.
            </p>
          </div>

          <div>
            <h3 className="text-sm font-semibold text-accent">
              Auto-generated GraphQL API
            </h3>
            <p className="mt-2 text-sm leading-relaxed text-text-secondary">
              Tag a pipeline with [TraxMutation] and it gets a typed GraphQL
              mutation. [TraxQuery] makes a query. Real-time lifecycle events
              via WebSocket subscriptions.
            </p>
          </div>

          <div>
            <h3 className="text-sm font-semibold text-accent">
              Monitoring dashboard
            </h3>
            <p className="mt-2 text-sm leading-relaxed text-text-secondary">
              A Blazor Server UI that mounts into your existing app. View
              executions, inspect failures, requeue jobs, or run pipelines
              with entirely new inputs.
            </p>
          </div>
        </div>
      </div>
    </section>
  );
}
