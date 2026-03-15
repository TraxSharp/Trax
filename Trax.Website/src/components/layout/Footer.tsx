import Link from "next/link";

export default function Footer() {
  return (
    <footer className="border-t border-border">
      <div className="mx-auto max-w-6xl px-6 py-12 sm:px-8">
        <div className="flex flex-col gap-8 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <span className="font-bold text-accent">Trax</span>
            <p className="mt-1 max-w-xs text-sm text-text-muted">
              Railway Oriented Programming for .NET. MIT licensed.
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
              <Link
                href="/blog"
                className="block text-text-muted hover:text-text-secondary"
              >
                Blog
              </Link>
            </div>
            <div className="space-y-2">
              <a
                href="https://github.com/TraxSharp"
                target="_blank"
                rel="noopener noreferrer"
                className="block text-text-muted hover:text-text-secondary"
              >
                GitHub
              </a>
              <a
                href="https://www.nuget.org/profiles/TraxSharp"
                target="_blank"
                rel="noopener noreferrer"
                className="block text-text-muted hover:text-text-secondary"
              >
                NuGet
              </a>
              <Link
                href="/about"
                className="block text-text-muted hover:text-text-secondary"
              >
                About
              </Link>
            </div>
          </div>
        </div>
      </div>
    </footer>
  );
}
