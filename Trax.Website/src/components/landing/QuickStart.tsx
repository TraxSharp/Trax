import Link from "next/link";

export default function QuickStart() {
  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">
          Start here.
        </h2>

        <div className="mt-8 max-w-lg">
          <p className="text-sm text-text-muted">Scaffold a full project:</p>
          <div className="mt-3 border-l-2 border-accent/30 bg-bg-secondary py-4 pl-5 pr-5 font-mono text-sm">
            <div className="text-text-muted">$ dotnet new install Trax.Samples.Templates</div>
            <div className="mt-1 text-text-primary">$ dotnet new trax-server -n MyApp</div>
          </div>

          <p className="mt-8 text-sm text-text-muted">Or add one package at a time:</p>
          <div className="mt-3 border-l-2 border-border bg-bg-secondary py-4 pl-5 pr-5 font-mono text-sm text-text-primary">
            $ dotnet add package Trax.Core
          </div>

          <div className="mt-8">
            <Link
              href="/docs/getting-started"
              className="text-sm font-medium text-accent hover:text-accent-hover"
            >
              Read the getting started guide &rarr;
            </Link>
          </div>
        </div>
      </div>
    </section>
  );
}
