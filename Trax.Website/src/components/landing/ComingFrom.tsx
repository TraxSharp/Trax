const origins = [
  {
    from: "MediatR",
    body: "A request handler becomes a train, and sending a request becomes ITrainBus.RunAsync. The difference is what happens around the call: the run is recorded, the chain is checked when the host starts, and the same train can later be scheduled or served over GraphQL without changing it.",
  },
  {
    from: "Hangfire or Quartz.NET",
    body: "A job becomes a train and a recurring job becomes a manifest. Workers coordinate through Postgres, so there is no separate storage package to pick. Queued work is stored by the train's interface name, not a serialized method call, so refactoring the implementation does not break jobs already in the queue. Dependent jobs and dead letters are included.",
  },
  {
    from: "Temporal",
    body: "Trax runs inside your application on the Postgres you already have, so there is no cluster to operate and your code has no determinism rules to follow. The trade today is that a train which dies mid-run starts again from its first step, where Temporal would resume it.",
  },
];

export default function ComingFrom() {
  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">
          Coming from something else
        </h2>

        <div className="mt-10 grid gap-10 lg:grid-cols-3">
          {origins.map((o) => (
            <div key={o.from}>
              <h3 className="text-sm font-semibold text-accent">{o.from}</h3>
              <p className="mt-2 text-sm leading-relaxed text-text-secondary">
                {o.body}
              </p>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
