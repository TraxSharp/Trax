import Link from "next/link";

export default function QuickStart() {
  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">Start from an existing API</h2>
        <p className="mt-3 max-w-2xl text-text-secondary">
          Point <code className="font-mono text-[0.95em]">trax generate</code> at the OpenAPI or GraphQL spec you
          already have. It writes a hub and one train per operation, and leaves the steps for you to fill in.
        </p>

        <div className="mt-6 max-w-2xl overflow-x-auto border-l-2 border-accent/30 bg-bg-secondary px-5 py-4 font-mono text-sm text-text-primary">
          $ trax generate --schema ./payments-api.json --output ./Payments --name Payments
        </div>

        <Link href="/docs/reference/cli" className="mt-6 inline-block text-sm font-medium text-accent hover:text-accent-hover">
          Everything trax generate does &rarr;
        </Link>
      </div>
    </section>
  );
}
