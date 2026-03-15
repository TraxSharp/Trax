import Link from "next/link";

export default function ComparisonTable() {
  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <div className="lg:flex lg:items-start lg:justify-between lg:gap-16">
          <div className="max-w-sm">
            <h2 className="text-2xl font-semibold text-text-primary">
              Not a replacement for Quartz or Hangfire.
            </h2>
            <p className="mt-3 text-text-secondary">
              Trax solves a different problem — composable, type-safe
              multi-step workflows with built-in observability. It can
              even use Hangfire as a backend.
            </p>
            <Link
              href="/docs/reference/comparison"
              className="mt-4 inline-block text-sm text-accent hover:text-accent-hover"
            >
              Full comparison &rarr;
            </Link>
          </div>

          <div className="mt-8 flex-1 overflow-x-auto lg:mt-0">
            <table className="w-full text-left text-sm">
              <thead>
                <tr className="border-b border-border">
                  <th className="pb-3 pr-8 font-normal text-text-muted" />
                  <th className="pb-3 pr-8 font-medium text-accent">Trax</th>
                  <th className="pb-3 pr-8 font-normal text-text-muted">
                    Quartz
                  </th>
                  <th className="pb-3 font-normal text-text-muted">
                    Hangfire
                  </th>
                </tr>
              </thead>
              <tbody className="text-text-secondary">
                <Row
                  label="Multi-step composition"
                  trax="Chain API"
                  quartz="Manual"
                  hangfire="Continuations"
                />
                <Row
                  label="Type-safe pipelines"
                  trax="Compile-time"
                  quartz="No"
                  hangfire="No"
                />
                <Row
                  label="Error propagation"
                  trax="Automatic"
                  quartz="try/catch"
                  hangfire="try/catch"
                />
                <Row
                  label="Job dependencies"
                  trax="DAG"
                  quartz="No"
                  hangfire="Linear"
                />
                <Row
                  label="Step-level progress"
                  trax="Built-in"
                  quartz="No"
                  hangfire="No"
                />
                <Row
                  label="CancellationToken"
                  trax="First-class"
                  quartz="InterruptableJob"
                  hangfire="Filter"
                />
                <Row
                  label="GraphQL API"
                  trax="Auto-generated"
                  quartz="No"
                  hangfire="No"
                />
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </section>
  );
}

function Row({
  label,
  trax,
  quartz,
  hangfire,
}: {
  label: string;
  trax: string;
  quartz: string;
  hangfire: string;
}) {
  return (
    <tr className="border-b border-border/50">
      <td className="py-2.5 pr-8 text-text-muted">{label}</td>
      <td className="py-2.5 pr-8 text-text-primary">{trax}</td>
      <td className="py-2.5 pr-8">{quartz}</td>
      <td className="py-2.5">{hangfire}</td>
    </tr>
  );
}
