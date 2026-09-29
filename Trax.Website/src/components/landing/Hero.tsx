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
            Business logic you can call, schedule, or serve as an API
          </h1>
          <p className="mt-6 text-lg leading-relaxed text-text-secondary">
            A train is a typed pipeline of small steps. Call it from a
            controller, put it on a cron schedule, send it to a worker on
            another machine, or publish it as a GraphQL mutation.
          </p>
          <p className="mt-4 text-text-muted">
            It is the same class each time, and every run is written to your
            Postgres: when it started, how it ended, and which step failed.
          </p>
          <div className="mt-10 flex items-center gap-4">
            <Link
              href="/docs/getting-started"
              className="rounded-md bg-accent px-5 py-2.5 text-sm font-medium text-white transition-colors hover:bg-accent-hover"
            >
              Get started
            </Link>
            <a
              href="https://github.com/TraxSharp"
              target="_blank"
              rel="noopener noreferrer"
              className="text-sm font-medium text-text-muted transition-colors hover:text-text-secondary"
            >
              github.com/TraxSharp &rarr;
            </a>
          </div>
          <p className="mt-8 font-mono text-xs text-text-muted">
            $ dotnet add package Trax.Core
          </p>
        </div>

        {/* Right: visual pipeline diagram */}
        <div className="mt-12 lg:mt-2 lg:flex-1">
          <pre className="overflow-x-auto font-mono text-[13px] leading-relaxed text-text-muted">
            <span className="text-accent">{"  Input"}</span>
            {`
    │
    ▼`}
            {"\n"}
            <span className="text-accent-bright">{"  ┌─ CheckInventory ─┐"}</span>
            {`
  │                   │`}
            {"\n"}
            <span className="text-accent-bright">{"  ├─ ChargePayment ──┤"}</span>
            {`
  │                   │`}
            {"\n"}
            <span className="text-accent-bright">{"  ├─ CreateShipment ─┤"}</span>
            {`
  │                   │`}
            {"\n"}
            <span className="text-accent-bright">{"  ▼"}</span>{"                   "}
            <span className="text-derail">{"▼"}</span>
            {"\n"}
            <span className="text-accent-bright">{"  Output"}</span>
            {"             "}
            <span className="text-derail">{"Exception"}</span>
            {"\n\n"}
            <span className="text-text-muted/60">{"  success             failure"}</span>
          </pre>
        </div>
      </div>
    </section>
  );
}
