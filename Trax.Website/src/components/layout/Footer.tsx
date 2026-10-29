import Link from "next/link";
import TraxLogo from "./TraxLogo";

export default function Footer() {
  return (
    <footer className="border-t border-border">
      <div className="mx-auto max-w-6xl px-6 py-12 sm:px-8">
        <div className="flex flex-col gap-8 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <TraxLogo maskId="footer" className="h-7 w-auto text-brand" />
            <p className="mt-1 max-w-xs text-sm text-text-muted">
              The backend that checks the client&apos;s work. MIT licensed.
            </p>
            <p className="mt-3 max-w-xs text-sm text-text-muted">
              Sponsored by{" "}
              <a
                href="https://constituentvoice.com/"
                target="_blank"
                rel="noopener noreferrer"
                className="text-text-secondary hover:text-accent"
              >
                Constituent Voice
              </a>
            </p>
          </div>

          <div className="flex gap-12 text-sm">
            <div className="space-y-2">
              <Link
                href="/docs"
                className="block text-text-muted hover:text-text-secondary"
              >
                Docs
              </Link>
              <Link
                href="/docs/getting-started"
                className="block text-text-muted hover:text-text-secondary"
              >
                Getting Started
              </Link>
            </div>
            <div className="space-y-2">
              <a
                href="https://github.com/TraxSharp/Trax"
                target="_blank"
                rel="noopener noreferrer"
                className="block text-text-muted hover:text-text-secondary"
              >
                GitHub
              </a>
              <a
                href="https://www.nuget.org/profiles/Theauxm"
                target="_blank"
                rel="noopener noreferrer"
                className="block text-text-muted hover:text-text-secondary"
              >
                NuGet
              </a>
              <a
                href="https://github.com/sponsors/TraxSharp"
                target="_blank"
                rel="noopener noreferrer"
                className="block text-text-muted hover:text-text-secondary"
              >
                Sponsor
              </a>
            </div>
          </div>
        </div>
      </div>
    </footer>
  );
}
