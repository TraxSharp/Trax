import Link from "next/link";

export default function Hero() {
  return (
    <section className="relative overflow-hidden border-b border-border">
      <div className="mx-auto max-w-6xl px-6 pb-20 pt-24 sm:px-8 lg:flex lg:items-start lg:gap-16 lg:pt-32">
        {/* Left: text, left-aligned */}
        <div className="max-w-xl">
          <p className="font-mono text-sm tracking-wider text-accent">
            MIT licensed. .NET 10. Runs inside your ASP.NET app.
          </p>
          <h1 className="mt-4 text-4xl font-bold leading-[1.15] tracking-tight text-text-primary sm:text-5xl">
            A headless web framework for .NET
          </h1>
          <p className="mt-6 text-lg leading-relaxed text-text-secondary">
            Every mutation in your app is an event on a state machine you
            define once in C#. Trax applies each event on the server and
            generates the client from the same definition, so your frontend is
            only views.
          </p>
          <p className="mt-4 text-text-muted">
            Behind it, Trax serves the GraphQL API with auth and live updates,
            runs the work on schedules and workers, and records every run in
            your Postgres. The client is generated for React today.{" "}
            <Link href="/roadmap" className="text-accent hover:text-accent-bright">
              More frameworks are on the roadmap &rarr;
            </Link>
          </p>
          <div className="mt-10 flex items-center gap-4">
            <Link
              href="/docs/getting-started"
              className="rounded-md bg-accent px-5 py-2.5 text-sm font-medium text-white transition-colors hover:bg-accent-hover"
            >
              Get started
            </Link>
            <a
              href="https://github.com/TraxSharp/Trax"
              target="_blank"
              rel="noopener noreferrer"
              className="text-sm font-medium text-text-muted transition-colors hover:text-text-secondary"
            >
              github.com/TraxSharp/Trax &rarr;
            </a>
          </div>
          <p className="mt-8 font-mono text-xs text-text-muted">
            $ dotnet add package Trax.Core
          </p>
        </div>

        {/* Right: one machine, applied on the server and generated into the client */}
        <div className="mt-12 lg:mt-2 lg:flex-1">
          <pre className="overflow-x-auto font-mono text-[13px] leading-relaxed text-text-muted">
            <span className="text-accent">{"  Your state machine"}</span>
            {`
    │
    ├──▶ `}
            <span className="text-accent-bright">{"Server"}</span>
            {"      applies each event"}
            {`
    │
    └──▶ `}
            <span className="text-accent-bright">{"Client"}</span>
            {"      generated"}
            {`
           │
           ▼
         `}
            <span className="text-accent">{"Your views"}</span>
            {"\n\n"}
            <span className="text-text-muted/60">{"  you write the first and the last"}</span>
          </pre>
        </div>
      </div>
    </section>
  );
}
