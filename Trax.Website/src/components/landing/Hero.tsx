import Link from "next/link";

export default function Hero() {
  return (
    <section className="relative overflow-hidden border-b border-border">
      <div className="mx-auto max-w-6xl px-6 pb-20 pt-24 sm:px-8 lg:flex lg:items-start lg:gap-16 lg:pt-32">
        {/* Left — text, left-aligned */}
        <div className="max-w-xl">
          <p className="font-mono text-sm tracking-wider text-accent">
            MIT licensed.
          </p>
          <h1 className="mt-4 text-4xl font-bold leading-[1.15] tracking-tight text-text-primary sm:text-5xl">
            A framework for
            <br />
            readable .NET
          </h1>
          <p className="mt-6 text-lg leading-relaxed text-text-secondary">
            Single responsibility code is hard to write. Business requirements
            lose their meaning somewhere between controllers, handlers,
            services, and routing. Side effects begin to bleed across these
            boundaries. Code becomes mismanaged, and difficult to understand.
            Without a strong code style, readability stops being a priority.
          </p>
          <p className="mt-4 text-text-muted">
            Trax consolidates all of that into one framework. Your configuration
            lives in Program.cs, your logic lives in pipelines, and everything
            reads like a language your team already understands.
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

        {/* Right — visual pipeline diagram */}
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
