import Link from "next/link";

export default function Hero() {
  return (
    <section className="relative overflow-hidden border-b border-border">
      <div className="mx-auto max-w-6xl px-6 pb-16 pt-24 sm:px-8 lg:pt-32">
        <div className="max-w-2xl">
          <h1 className="text-4xl font-bold leading-[1.15] tracking-tight text-text-primary sm:text-5xl">
            a headless web framework for .NET
          </h1>
          <p className="mt-6 text-lg leading-relaxed text-text-secondary">
            Whatever goes wrong, the backend gets it right.
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
      </div>
    </section>
  );
}
