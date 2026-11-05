import Link from "next/link";
import { Arrow, Box } from "./Diagram";

interface Architecture {
  name: string;
  description: string;
  // The processes, left to right. A row of several is one process holding them all.
  processes: { label: string; onDemand?: boolean }[][];
}

// The five ways the scheduler docs describe splitting a Trax app, drawn as its processes over the one database they
// share. The configuration for each is on the linked page.
const architectures: Architecture[] = [
  {
    name: "All-in-one",
    description: "API, scheduler and the jobs they run in a single process.",
    processes: [[{ label: "API" }, { label: "Scheduler" }, { label: "Jobs" }]],
  },
  {
    name: "API + Scheduler",
    description: "The API queues work, and a separate scheduler picks it up and runs it.",
    processes: [[{ label: "API" }], [{ label: "Scheduler" }]],
  },
  {
    name: "Hub + Workers",
    description: "A hub serves the API and schedules. Workers take jobs from the database and run them.",
    processes: [[{ label: "Hub" }], [{ label: "Worker" }], [{ label: "Worker" }], [{ label: "Worker" }]],
  },
  {
    name: "Ephemeral dispatch",
    description: "The API hands work to an AWS Lambda function, which runs it and stops.",
    processes: [[{ label: "API" }], [{ label: "Lambda", onDemand: true }]],
  },
  {
    name: "Just the scheduler",
    description: "No API at all. One process runs trains on a schedule.",
    processes: [[{ label: "Scheduler" }]],
  },
];

function Topology({ arch }: { arch: Architecture }) {
  const lambda = arch.processes.some((p) => p.some((x) => x.onDemand));
  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center gap-2">
        {arch.processes.map((process, i) => (
          <div key={i} className="contents">
            {lambda && i > 0 && <Arrow direction="right" />}
            {process.length > 1 ? (
              <div className="flex flex-1 gap-1.5 rounded-md border border-border p-1.5">
                {process.map((p) => (
                  <Box key={p.label} label={p.label} className="flex-1 px-1.5 py-1.5" />
                ))}
              </div>
            ) : (
              <Box label={process[0].label} tone={process[0].onDemand ? "muted" : "plain"} className="flex-1 px-1.5" />
            )}
          </div>
        ))}
      </div>
      <Arrow direction="down" />
      <Box label="Postgres" tone="accent" className="py-1.5" />
    </div>
  );
}

export default function DeploymentArchitectures() {
  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">Deploy it however you want</h2>
        <p className="mt-3 max-w-2xl text-text-secondary">
          Start in one process and split it out as load grows, without rewriting your trains.
        </p>
        <Link href="/docs/scheduler/remote-execution" className="mt-4 inline-block text-sm font-medium text-accent hover:text-accent-hover">
          Running work on other machines &rarr;
        </Link>

        <div className="mt-10 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {architectures.map((arch, i) => (
            <div key={arch.name} className="flex flex-col gap-4 rounded-lg border border-border/60 bg-bg-secondary/40 p-5">
              <div>
                <div className="flex items-baseline gap-3">
                  <span className="font-mono text-xs text-text-muted">{String(i + 1).padStart(2, "0")}</span>
                  <h3 className="font-semibold text-text-primary">{arch.name}</h3>
                </div>
                <p className="mt-1.5 text-sm leading-relaxed text-text-secondary">{arch.description}</p>
              </div>
              <div className="mt-auto">
                <Topology arch={arch} />
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
