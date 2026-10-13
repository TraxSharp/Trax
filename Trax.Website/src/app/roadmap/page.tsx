import type { Metadata } from "next";
import Link from "next/link";
import roadmap from "@/data/roadmap.json";

export const metadata: Metadata = {
  title: "Roadmap",
  description:
    "Where Trax .NET is going: from a spec to a running API and a small generated client.",
};

type Status = "done" | "building" | "next" | "later";

interface Milestone {
  key: string;
  status: Status;
  title: string;
  points: string[];
  code?: { caption: string; text: string };
}

const statusLabel: Record<Status, string> = {
  done: "Shipped",
  building: "Building now",
  next: "Next",
  later: "Later",
};

const statusStyle: Record<Status, string> = {
  done: "border-success/40 bg-success/10 text-success",
  building: "border-accent/40 bg-accent-muted text-accent-bright",
  next: "border-info/40 bg-info/10 text-info",
  later: "border-border-hover bg-bg-tertiary text-text-muted",
};

const dotStyle: Record<Status, string> = {
  done: "bg-success",
  building: "bg-accent-bright",
  next: "bg-info",
  later: "bg-text-muted",
};

// The end state, left to right. `by` names the milestone that delivers each piece, whose status the card shows.
const pipeline = [
  { title: "Your spec", body: "OpenAPI or GraphQL", by: "spec-to-app" },
  { title: "Trax API", body: "Trains, machines, persisted operations, live updates", by: "machines-api" },
  { title: "Generated client", body: "Typed operations and machines", by: "clients" },
  { title: "Your views", body: "The only frontend code you write", by: null },
];

export default function RoadmapPage() {
  const milestones = roadmap.milestones as Milestone[];
  const statusOf = (key: string) => milestones.find((m) => m.key === key)?.status;

  return (
    <main>
      <section className="border-b border-border">
        <div className="mx-auto max-w-6xl px-6 pb-16 pt-20 sm:px-8">
          <p className="font-mono text-sm tracking-wider text-accent">Roadmap</p>
          <h1 className="mt-4 max-w-3xl text-4xl font-bold leading-[1.15] tracking-tight text-text-primary sm:text-5xl">
            From a spec to a running API and a small client
          </h1>
          <p className="mt-6 max-w-2xl text-lg text-text-secondary">
            What has shipped, what is next, and what comes after. No dates.
          </p>

          <ol className="mt-12 grid gap-3 md:grid-cols-4">
            {pipeline.map((step, i) => (
              <li key={step.title} className="relative">
                <div className="h-full rounded-lg border border-border bg-bg-card p-4">
                  <p className="font-mono text-xs text-text-muted">{i + 1}</p>
                  <h2 className="mt-1 text-sm font-semibold text-text-primary">
                    {step.title}
                  </h2>
                  <p className="mt-2 text-sm leading-relaxed text-text-secondary">
                    {step.body}
                  </p>
                  {step.by && statusOf(step.by) && (
                    <span
                      className={`mt-3 inline-block rounded-full border px-2 py-0.5 font-mono text-[11px] ${statusStyle[statusOf(step.by)!]}`}
                    >
                      {statusLabel[statusOf(step.by)!]}
                    </span>
                  )}
                </div>
                {i < pipeline.length - 1 && (
                  <span
                    aria-hidden
                    className="absolute -bottom-3 left-6 text-text-muted md:-right-3 md:bottom-auto md:left-auto md:top-1/2 md:-translate-y-1/2"
                  >
                    <span className="md:hidden">&darr;</span>
                    <span className="hidden md:inline">&rarr;</span>
                  </span>
                )}
              </li>
            ))}
          </ol>
        </div>
      </section>

      <section className="border-b border-border py-16">
        <div className="mx-auto max-w-6xl px-6 sm:px-8">
          <h2 className="text-2xl font-semibold text-text-primary">Available today</h2>
          <ul className="mt-8 grid gap-x-12 gap-y-3 sm:grid-cols-2">
            {roadmap.today.map((item) => (
              <li key={item.href} className="flex items-baseline gap-3">
                <span aria-hidden className="text-accent">&#10003;</span>
                <Link
                  href={item.href}
                  className="text-sm text-text-secondary underline-offset-4 hover:text-text-primary hover:underline"
                >
                  {item.label}
                </Link>
              </li>
            ))}
          </ul>
        </div>
      </section>

      <section className="py-16">
        <div className="mx-auto max-w-6xl px-6 sm:px-8">
          <h2 className="text-2xl font-semibold text-text-primary">Milestones</h2>
          <ol className="mt-10 border-l border-border">
            {milestones.map((m) => (
              <li key={m.key} id={m.key} className="relative scroll-mt-24 pb-14 pl-8 last:pb-0">
                <span
                  aria-hidden
                  className={`absolute -left-[5px] top-2 h-2.5 w-2.5 rounded-full ${dotStyle[m.status]}`}
                />
                <div className="grid gap-8 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
                  <div>
                    <span
                      className={`inline-block rounded-full border px-2.5 py-0.5 font-mono text-xs ${statusStyle[m.status]}`}
                    >
                      {statusLabel[m.status]}
                    </span>
                    <h3 className="mt-3 text-xl font-semibold text-text-primary">{m.title}</h3>
                    <ul className="mt-4 space-y-2">
                      {m.points.map((p) => (
                        <li key={p} className="flex gap-3 text-sm text-text-secondary">
                          <span aria-hidden className="text-text-muted">&ndash;</span>
                          {p}
                        </li>
                      ))}
                    </ul>
                  </div>
                  {m.code && (
                    <div className="min-w-0">
                      <p className="text-xs text-text-muted">{m.code.caption}</p>
                      <pre className="mt-2 overflow-x-auto border-l-2 border-accent/30 bg-bg-secondary py-4 pl-5 pr-5 font-mono text-[13px] leading-relaxed text-text-primary">
                        {m.code.text}
                      </pre>
                    </div>
                  )}
                </div>
              </li>
            ))}
          </ol>

        </div>
      </section>
    </main>
  );
}
