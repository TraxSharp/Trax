const limits = [
  {
    title: "A crash restarts the train",
    body: "If a process dies halfway through a run, the run is marked failed and a scheduled train is retried from its first junction. Junctions that call other systems should be safe to repeat. Resuming at the step that failed is being built.",
  },
  {
    title: "Postgres in production",
    body: "The scheduler coordinates workers with Postgres row locks and advisory locks. SQLite works for a single process and in-memory storage works for tests. There is no SQL Server or MySQL provider.",
  },
  {
    title: "Not a message bus",
    body: "Trax runs work and records it. It does not carry messages between services through a broker, or coordinate sagas across them. If that is the heart of your system, a service bus is the better fit.",
  },
  {
    title: "One language, one region",
    body: "Trains are C#, and one Postgres database coordinates them. Workflows written in several languages, or replicated across regions, are outside what Trax does.",
  },
  {
    title: "No tracing export yet",
    body: "Runs are recorded in the database and logged through ILogger. They are not emitted as OpenTelemetry spans.",
  },
];

export default function WhereTraxStops() {
  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">
          Where Trax stops
        </h2>
        <p className="mt-3 max-w-2xl text-text-secondary">
          What it does not do today, so you can decide before you install it.
        </p>

        <div className="mt-10 grid gap-x-16 gap-y-8 sm:grid-cols-2">
          {limits.map((limit) => (
            <div key={limit.title}>
              <h3 className="text-sm font-semibold text-text-primary">
                {limit.title}
              </h3>
              <p className="mt-2 text-sm leading-relaxed text-text-secondary">
                {limit.body}
              </p>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
