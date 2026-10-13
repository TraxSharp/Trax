import Link from "next/link";

// `roadmap` links a limit to the milestone on /roadmap that lifts it.
const limits: {
  title: string;
  body: string;
  roadmap?: { key: string; label: string };
}[] = [
  {
    title: "A crash reruns from the last checkpoint",
    body: "If a process dies halfway through a run, the run is marked failed and retried from its last checkpoint, or from its first junction if it declares none. Junctions after that point run again, so ones that call other systems should be safe to repeat.",
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
    <section id="where-trax-stops" className="scroll-mt-16 border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">
          Where Trax stops
        </h2>
        <p className="mt-3 max-w-2xl text-text-secondary">
          What it does not do today, so you can decide before you install it.
        </p>
        <Link href="/docs/reference/comparison" className="mt-4 inline-block text-sm font-medium text-accent hover:text-accent-hover">
          How Trax compares with Quartz.NET and Hangfire &rarr;
        </Link>

        <div className="mt-10 grid gap-x-16 gap-y-8 sm:grid-cols-2">
          {limits.map((limit) => (
            <div key={limit.title}>
              <h3 className="text-sm font-semibold text-text-primary">
                {limit.title}
              </h3>
              <p className="mt-2 text-sm leading-relaxed text-text-secondary">
                {limit.body}
              </p>
              {limit.roadmap && (
                <Link
                  href={`/roadmap#${limit.roadmap.key}`}
                  className="mt-2 inline-block text-sm text-accent hover:text-accent-bright"
                >
                  {limit.roadmap.label} &rarr;
                </Link>
              )}
            </div>
          ))}
        </div>

        <p className="mt-12 text-sm text-text-muted">
          Where Trax is going next:{" "}
          <Link href="/roadmap" className="text-accent hover:text-accent-bright">
            the roadmap &rarr;
          </Link>
        </p>
      </div>
    </section>
  );
}
