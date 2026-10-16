import type { Metadata } from "next";
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
  shipped?: string;
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

function Points({ points }: { points: string[] }) {
  return (
    <ul className="mt-3 space-y-1.5">
      {points.map((p) => (
        <li key={p} className="flex gap-3 text-sm text-text-secondary">
          <span aria-hidden className="text-text-muted">&ndash;</span>
          {p}
        </li>
      ))}
    </ul>
  );
}

export default function RoadmapPage() {
  const milestones = roadmap.milestones as Milestone[];
  const ahead = milestones.filter((m) => m.status !== "done");
  // Newest first; roadmap.json lists them oldest first.
  const shipped = milestones.filter((m) => m.status === "done").reverse();

  return (
    <main>
      <section className="border-b border-border">
        <div className="mx-auto max-w-6xl px-6 pb-10 pt-16 sm:px-8">
          <p className="font-mono text-sm tracking-wider text-accent">Roadmap</p>
          <h1 className="mt-4 max-w-3xl text-4xl font-bold leading-[1.15] tracking-tight text-text-primary sm:text-5xl">
            Where Trax is headed
          </h1>
        </div>
      </section>

      <section className="border-b border-border py-12">
        <div className="mx-auto max-w-6xl px-6 sm:px-8">
          <h2 className="text-2xl font-semibold text-text-primary">Ahead</h2>
          <div className="mt-8 grid gap-6 lg:grid-cols-2">
            {ahead.map((m) => (
              <article
                key={m.key}
                id={m.key}
                className="flex min-w-0 scroll-mt-24 flex-col rounded-lg border border-border bg-bg-card p-6"
              >
                <span
                  className={`self-start rounded-full border px-2.5 py-0.5 font-mono text-xs ${statusStyle[m.status]}`}
                >
                  {statusLabel[m.status]}
                </span>
                <h3 className="mt-3 text-lg font-semibold text-text-primary">{m.title}</h3>
                <Points points={m.points} />
                {m.code && (
                  <div className="mt-5 min-w-0">
                    <p className="text-xs text-text-muted">{m.code.caption}</p>
                    <pre className="mt-2 overflow-x-auto border-l-2 border-accent/30 bg-bg-secondary px-4 py-3 font-mono text-[12px] leading-relaxed text-text-primary">
                      {m.code.text}
                    </pre>
                  </div>
                )}
              </article>
            ))}
          </div>
        </div>
      </section>

      <section className="py-12">
        <div className="mx-auto max-w-6xl px-6 sm:px-8">
          <h2 className="text-2xl font-semibold text-text-primary">Shipped</h2>
          <div className="mt-8 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {shipped.map((m) => (
              <article
                key={m.key}
                id={m.key}
                className="min-w-0 scroll-mt-24 rounded-lg border border-border bg-bg-card p-5"
              >
                <p className="flex items-center gap-2 font-mono text-xs text-text-muted">
                  <span aria-hidden className="h-2 w-2 rounded-full bg-success" />
                  {m.shipped}
                </p>
                <h3 className="mt-2 font-semibold text-text-primary">{m.title}</h3>
                <Points points={m.points} />
                {m.code && (
                  <details className="mt-4 min-w-0">
                    <summary className="cursor-pointer text-xs text-text-muted hover:text-text-primary">
                      Code
                    </summary>
                    <pre className="mt-2 overflow-x-auto border-l-2 border-accent/30 bg-bg-secondary px-4 py-3 font-mono text-[12px] leading-relaxed text-text-primary">
                      {m.code.text}
                    </pre>
                  </details>
                )}
              </article>
            ))}
          </div>
        </div>
      </section>

    </main>
  );
}
