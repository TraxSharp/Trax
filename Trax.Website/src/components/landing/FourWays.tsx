import Link from "next/link";
import { Arrow, Box, Figure } from "./Diagram";

const starters = [
  { label: "A request", note: "GraphQL or your own controller" },
  { label: "A schedule", note: "every 5 minutes, with retries" },
  { label: "A state machine", note: "when an order is placed" },
  { label: "A worker", note: "on another machine" },
];

// One train and the four things that can start it. The code for each is on the linked page.
export default function FourWays() {
  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">One train, four ways to start it</h2>
        <p className="mt-3 max-w-2xl text-text-secondary">
          A train is a chain of steps the server runs. It is written once, and a request, a schedule, a state machine or
          a worker on another machine can start it.
        </p>
        <Link href="/docs/core/trains-and-junctions" className="mt-4 inline-block text-sm font-medium text-accent hover:text-accent-hover">
          How a train is built &rarr;
        </Link>

        <Figure caption="Every start runs the same steps and leaves the same record.">
          <div className="flex flex-col items-stretch gap-3 sm:flex-row sm:items-center">
            <div className="grid grid-cols-2 gap-3 sm:w-72 sm:grid-cols-1">
              {starters.map((s) => (
                <Box key={s.label} label={s.label} note={s.note} />
              ))}
            </div>
            <Arrow />
            <div className="flex flex-1 flex-col gap-3 rounded-md border border-accent/50 p-4">
              <span className="text-center text-sm font-medium text-accent-bright">RecalculateLeaderboard</span>
              <div className="flex flex-col items-stretch gap-2 sm:flex-row sm:items-center">
                <Box label="Aggregate scores" className="sm:flex-1" />
                <Arrow />
                <Box label="Rank players" className="sm:flex-1" />
              </div>
            </div>
          </div>
        </Figure>
      </div>
    </section>
  );
}
