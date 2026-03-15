const packages = [
  {
    name: "Trax.Core",
    desc: "Pipeline engine",
    nuget: "https://www.nuget.org/packages/Trax.Core/",
  },
  {
    name: "Trax.Effect",
    desc: "Execution tracking and DI integration",
    nuget: "https://www.nuget.org/packages/Trax.Effect/",
  },
  {
    name: "Trax.Mediator",
    desc: "Input routing and decoupling",
    nuget: "https://www.nuget.org/packages/Trax.Mediator/",
  },
  {
    name: "Trax.Scheduler",
    desc: "Job scheduling, retries, and dependencies",
    nuget: "https://www.nuget.org/packages/Trax.Scheduler/",
  },
  {
    name: "Trax.Dashboard",
    desc: "Monitoring UI",
    nuget: "https://www.nuget.org/packages/Trax.Dashboard/",
  },
  {
    name: "Trax.Api.GraphQL",
    desc: "GraphQL via HotChocolate",
    nuget: "https://www.nuget.org/packages/Trax.Api.GraphQL/",
  },
  {
    name: "Trax.Effect.Data.Postgres",
    desc: "PostgreSQL persistence",
    nuget: "https://www.nuget.org/packages/Trax.Effect.Data.Postgres/",
  },
  {
    name: "Trax.Samples.Templates",
    desc: "dotnet new project template",
    nuget: "https://www.nuget.org/packages/Trax.Samples.Templates/",
  },
];

export default function PackageList() {
  return (
    <section className="py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">
          Packages
        </h2>
        <p className="mt-2 text-text-muted">
          All MIT licensed. All on NuGet.
        </p>

        {/* Simple list, not cards */}
        <div className="mt-8 divide-y divide-border/50">
          {packages.map((pkg) => (
            <a
              key={pkg.name}
              href={pkg.nuget}
              target="_blank"
              rel="noopener noreferrer"
              className="group flex items-baseline justify-between py-3"
            >
              <div className="flex items-baseline gap-3">
                <span className="font-mono text-sm text-text-primary group-hover:text-accent">
                  {pkg.name}
                </span>
                <span className="text-xs text-text-muted">{pkg.desc}</span>
              </div>
              <img
                src={`https://img.shields.io/nuget/v/${pkg.name}?style=flat-square&color=3d8b37&label=`}
                alt={`${pkg.name} version`}
                className="h-4 flex-shrink-0 opacity-60 group-hover:opacity-100"
              />
            </a>
          ))}
        </div>
      </div>
    </section>
  );
}
