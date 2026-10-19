import { Link, useLocation } from "react-router-dom";

// The views of Runs (api/0043): every run, the failed ones, what waits in the queue, and the dead
// letters. Failed is All runs filtered to state FAILED, so it is the same page with ?state=FAILED.

export type RunsView = "all" | "failed" | "queued" | "deadLetters";

const VIEWS: { view: RunsView; label: string; to: string }[] = [
  { view: "all", label: "All", to: "/runs" },
  { view: "failed", label: "Failed", to: "/runs?state=FAILED" },
  { view: "queued", label: "Queued", to: "/runs/queued" },
  { view: "deadLetters", label: "Dead letters", to: "/runs/dead-letters" },
];

/** The view a Runs list route shows. */
function runsViewOf(pathname: string, search: string): RunsView {
  if (pathname.startsWith("/runs/queued")) return "queued";
  if (pathname.startsWith("/runs/dead-letters")) return "deadLetters";
  return new URLSearchParams(search).get("state") === "FAILED" ? "failed" : "all";
}

export function RunsTabs() {
  const { pathname, search } = useLocation();
  const current = runsViewOf(pathname, search);
  return (
    <nav aria-label="Run views" className="flex gap-1 border-b border-line mb-5 text-sm overflow-x-auto">
      {VIEWS.map((v) => {
        const active = v.view === current;
        return (
          <Link
            key={v.view}
            to={v.to}
            aria-current={active ? "page" : undefined}
            className={`px-3 py-2 -mb-px border-b-2 whitespace-nowrap ${
              active
                ? "border-accent text-accent-fg font-medium"
                : "border-transparent text-muted hover:text-fg hover:border-line-strong"
            }`}
          >
            {v.label}
          </Link>
        );
      })}
    </nav>
  );
}
