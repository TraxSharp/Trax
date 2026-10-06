interface Pkg {
  name: string;
  desc: string;
}

interface Repo {
  repo: string;
  summary: string;
  packages: Pkg[];
}

const repos: Repo[] = [
  {
    repo: "Trax.Core",
    summary: "Trains, junctions and the chain",
    packages: [
      { name: "Trax.Core", desc: "Trains, junctions, decisions and railway error handling" },
      {
        name: "Trax.Core.Testing",
        desc: "Architecture-guard base classes and hygiene checks for NUnit",
      },
    ],
  },
  {
    repo: "Trax.Effect",
    summary: "Run recording, storage, decisions and state machines",
    packages: [
      {
        name: "Trax.Effect",
        desc: "Run recording, lifecycle hooks and dependency injection",
      },
      { name: "Trax.Effect.Data", desc: "The data layer the storage providers share" },
      { name: "Trax.Effect.Data.Postgres", desc: "PostgreSQL storage, for production" },
      { name: "Trax.Effect.Data.Sqlite", desc: "SQLite storage for a single process" },
      { name: "Trax.Effect.Data.InMemory", desc: "In-memory storage for tests" },
      { name: "Trax.Effect.Data.Testing", desc: "Architecture guards for the data layer" },
      {
        name: "Trax.Effect.Provider.Parameter",
        desc: "Stores each run's input and output",
      },
      { name: "Trax.Effect.Provider.Json", desc: "Logs state changes as JSON" },
      {
        name: "Trax.Effect.JunctionProvider.Logging",
        desc: "Logs each junction as it runs",
      },
      {
        name: "Trax.Effect.JunctionProvider.Progress",
        desc: "Records the running junction and checks for cancellation between junctions",
      },
      {
        name: "Trax.Effect.Decisions.SystemOne",
        desc: "Answers a train's decisions with Nimble or another System One model",
      },
      {
        name: "Trax.Effect.Broadcaster.RabbitMQ",
        desc: "Lifecycle events between processes over RabbitMQ",
      },
      {
        name: "Trax.Effect.Broadcaster.SignalR",
        desc: "Lifecycle events pushed to Blazor and JavaScript clients",
      },
      {
        name: "Trax.Effect.StateMachine",
        desc: "Portable snapshot state-machine engine",
      },
      {
        name: "Trax.Effect.StateMachine.Persistence",
        desc: "Postgres snapshot store and run-once effects",
      },
      {
        name: "Trax.Effect.StateMachine.Testing",
        desc: "Conformance tests between the C# and TypeScript engines",
      },
    ],
  },
  {
    repo: "Trax.Mediator",
    summary: "The train bus",
    packages: [
      {
        name: "Trax.Mediator",
        desc: "Dispatch by input type, and the chain check at startup",
      },
      {
        name: "Trax.Mediator.Testing",
        desc: "Architecture guards for trains and their interfaces",
      },
    ],
  },
  {
    repo: "Trax.Scheduler",
    summary: "Schedules, retries and workers",
    packages: [
      {
        name: "Trax.Scheduler",
        desc: "Schedules, retries, dead letters and local workers",
      },
      { name: "Trax.Scheduler.Sqs", desc: "Dispatch jobs through Amazon SQS" },
      { name: "Trax.Scheduler.Lambda", desc: "Dispatch jobs and runs to AWS Lambda" },
      { name: "Trax.Runner.Lambda", desc: "Base class for a Lambda function that runs trains" },
    ],
  },
  {
    repo: "Trax.Api",
    summary: "GraphQL, authentication and the client",
    packages: [
      { name: "Trax.Api", desc: "Train catalog, health checks and shared types" },
      { name: "Trax.Api.GraphQL", desc: "Typed GraphQL generated from your trains" },
      {
        name: "Trax.Api.GraphQL.PersistedOperations",
        desc: "Server-managed persisted operations",
      },
      { name: "Trax.Api.GraphQL.Audit", desc: "Request audit trail with a batched writer" },
      {
        name: "Trax.Api.GraphQL.Testing",
        desc: "Architecture guards for cross-schema edges",
      },
      {
        name: "Trax.Api.GraphQL.Client",
        desc: "GraphQL client checked against the schema at runtime, no codegen",
      },
      {
        name: "Trax.Api.GraphQL.Client.Typed",
        desc: "Queries whose selection set is a plain C# class",
      },
      {
        name: "Trax.Api.GraphQL.Client.Trax",
        desc: "Run GraphQL queries as junctions",
      },
      { name: "Trax.Api.Auth", desc: "The principal and claim types the auth packages share" },
      { name: "Trax.Api.Auth.ApiKey", desc: "API key authentication" },
      { name: "Trax.Api.Auth.Jwt", desc: "JWT bearer authentication" },
      { name: "Trax.Api.Auth.Jwt.Cognito", desc: "Amazon Cognito user pool tokens" },
      {
        name: "Trax.Api.Auth.Jwt.Cognito.Issuer",
        desc: "Mint Cognito-shaped access and ID tokens",
      },
      {
        name: "Trax.Api.Auth.Jwt.Testing",
        desc: "A local JWKS server and token minters for tests",
      },
      { name: "Trax.Api.Auth.Oidc", desc: "OpenID Connect sign-in with PKCE" },
    ],
  },
  {
    repo: "Trax.Dashboard",
    summary: "The monitoring UI",
    packages: [
      { name: "Trax.Dashboard", desc: "Blazor Server dashboard that mounts into your app" },
    ],
  },
  {
    repo: "Trax.Cli",
    summary: "The trax command",
    packages: [
      {
        name: "Trax.Cli",
        desc: "Project scaffolding from a schema, and state-machine codegen",
      },
    ],
  },
  {
    repo: "Trax.Samples",
    summary: "Templates",
    packages: [
      { name: "Trax.Samples.Templates", desc: "dotnet new templates for an API, a scheduler or a hub" },
    ],
  },
];

const total = repos.reduce((n, r) => n + r.packages.length, 0);

function Badge({ name }: { name: string }) {
  return (
    <img
      src={`https://img.shields.io/nuget/v/${name}?style=flat-square&color=3d8b37&label=`}
      alt={`${name} version`}
      className="h-4 flex-shrink-0 opacity-60 group-hover:opacity-100"
    />
  );
}

export default function PackageList() {
  return (
    <section className="py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">Packages</h2>
        <p className="mt-2 text-text-muted">
          {total} packages across {repos.length} repos, all on NuGet and all MIT
          licensed. There is no commercial edition, and there will not be one.
        </p>

        <div className="mt-8 divide-y divide-border/50 border-y border-border/50">
          {repos.map((r) => (
            <details key={r.repo} className="group/repo">
              <summary className="flex cursor-pointer list-none items-baseline justify-between gap-4 py-4 [&::-webkit-details-marker]:hidden">
                <div className="flex min-w-0 flex-wrap items-baseline gap-x-3 gap-y-1">
                  <span className="font-mono text-sm text-text-primary">{r.repo}</span>
                  <span className="text-xs text-text-muted">{r.summary}</span>
                </div>
                <span className="flex-shrink-0 text-xs text-text-muted">
                  {r.packages.length}{" "}
                  {r.packages.length === 1 ? "package" : "packages"}
                  <span className="ml-2 inline-block transition-transform group-open/repo:rotate-90">
                    &rsaquo;
                  </span>
                </span>
              </summary>

              <div className="pb-4 pl-4">
                {r.packages.map((pkg) => (
                  <a
                    key={pkg.name}
                    href={`https://www.nuget.org/packages/${pkg.name}/`}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="group flex items-baseline justify-between gap-4 py-2"
                  >
                    <div className="flex min-w-0 flex-wrap items-baseline gap-x-3 gap-y-1">
                      <span className="break-all font-mono text-[13px] text-text-secondary group-hover:text-accent">
                        {pkg.name}
                      </span>
                      <span className="text-xs text-text-muted">{pkg.desc}</span>
                    </div>
                    <Badge name={pkg.name} />
                  </a>
                ))}
              </div>
            </details>
          ))}
        </div>
      </div>
    </section>
  );
}
