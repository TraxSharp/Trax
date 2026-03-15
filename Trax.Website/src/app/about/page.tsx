import type { Metadata } from "next";
import Link from "next/link";

export const metadata: Metadata = {
  title: "About",
};

export default function AboutPage() {
  return (
    <div className="mx-auto max-w-3xl px-4 py-16 sm:px-6 lg:px-8">
      <h1 className="text-3xl font-bold text-text-primary">About Trax</h1>

      <div className="mt-8 space-y-8 text-text-secondary">
        <section>
          <h2 className="text-xl font-semibold text-text-primary">
            What is Trax?
          </h2>
          <p className="mt-3 leading-relaxed">
            Trax is a .NET framework for Railway Oriented Programming. It lets
            you build business logic as composable, type-safe pipelines where
            errors are handled automatically — no more scattered try/catch
            blocks or null checks burying your intent.
          </p>
        </section>

        <section>
          <h2 className="text-xl font-semibold text-text-primary">
            The Train Metaphor
          </h2>
          <p className="mt-3 leading-relaxed">
            Trax uses a train metaphor throughout. A <strong className="text-text-primary">Train</strong> is a
            pipeline that follows a route — a sequence of <strong className="text-text-primary">Junctions</strong> where
            work happens. At each junction, the train either continues on the{" "}
            <strong className="text-text-primary">right track</strong> (success) or switches to the{" "}
            <strong className="text-text-primary">left track</strong> (failure). A train never leaves the
            rails — it always reaches a destination. Data flows between junctions
            as <strong className="text-text-primary">Memory</strong>. Scheduled jobs use{" "}
            <strong className="text-text-primary">Manifests</strong>, and the monitoring UI is the{" "}
            <strong className="text-text-primary">Control Room</strong>.
          </p>
        </section>

        <section>
          <h2 className="text-xl font-semibold text-text-primary">History</h2>
          <p className="mt-3 leading-relaxed">
            Trax started as <strong className="text-text-primary">ChainSharp</strong>, a single-repo
            project for chaining service operations. It was split into a
            multi-repo workspace and renamed to Trax to better reflect the
            railway-oriented design and the train metaphor that had become
            central to the project.
          </p>
        </section>

        <section>
          <h2 className="text-xl font-semibold text-text-primary">
            Open Source
          </h2>
          <p className="mt-3 leading-relaxed">
            Trax is fully open source under the MIT License. All packages are
            published on NuGet. Contributions, bug reports, and feature
            requests are welcome on GitHub.
          </p>
          <div className="mt-4 flex gap-4">
            <a
              href="https://github.com/TraxSharp"
              target="_blank"
              rel="noopener noreferrer"
              className="rounded-lg border border-border px-4 py-2 text-sm text-text-secondary transition-colors hover:border-border-hover hover:text-text-primary"
            >
              GitHub
            </a>
            <a
              href="https://www.nuget.org/profiles/TraxSharp"
              target="_blank"
              rel="noopener noreferrer"
              className="rounded-lg border border-border px-4 py-2 text-sm text-text-secondary transition-colors hover:border-border-hover hover:text-text-primary"
            >
              NuGet
            </a>
            <Link
              href="/docs"
              className="rounded-lg border border-border px-4 py-2 text-sm text-text-secondary transition-colors hover:border-border-hover hover:text-text-primary"
            >
              Documentation
            </Link>
          </div>
        </section>

        <section>
          <h2 className="text-xl font-semibold text-text-primary">
            Acknowledgements
          </h2>
          <p className="mt-3 leading-relaxed">
            Without the help and guidance of Mark Keaton, Spencer Elkington,
            and Douglas Seely this project would not have been possible.
          </p>
        </section>
      </div>
    </div>
  );
}
