import Link from "next/link";
import AgentPrompt from "./AgentPrompt";

export default function Hero() {
  return (
    <section className="relative overflow-hidden border-b border-border">
      <div className="mx-auto max-w-6xl px-6 pb-8 pt-10 sm:px-8 lg:pt-12">
        <div className="grid items-center gap-12 lg:grid-cols-[minmax(0,1.2fr)_minmax(0,1fr)]">
          <div className="max-w-2xl">
            {/* Two lines from sm up, broken after "checks"; a phone wraps it as it needs to. */}
            <h1 className="text-4xl font-bold leading-[1.15] tracking-tight text-text-primary sm:text-5xl lg:text-[44px] xl:text-5xl">
              <span className="sm:block sm:whitespace-nowrap">the backend that checks</span>{" "}
              <span className="sm:block sm:whitespace-nowrap">the client&apos;s work</span>
            </h1>
            <p className="mt-6 text-lg leading-relaxed text-text-secondary">
              State machines and workflows the server enforces.
            </p>
            <div className="mt-10 flex items-center gap-4">
              <Link
                href="/docs/getting-started"
                className="rounded-md bg-accent px-5 py-2.5 text-sm font-medium text-white transition-colors hover:bg-accent-hover"
              >
                Get started
              </Link>
              <Link
                href="/docs/introduction"
                className="text-sm font-medium text-accent transition-colors hover:text-accent-hover"
              >
                Trax in five minutes &rarr;
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
            <p className="mt-8 font-mono text-xs leading-relaxed text-text-muted">
              $ dotnet new install Trax.Samples.Templates
              <br />
              $ dotnet new trax-hub -n MyApp
            </p>
          </div>
          <AgentPrompt />
        </div>
      </div>
    </section>
  );
}
