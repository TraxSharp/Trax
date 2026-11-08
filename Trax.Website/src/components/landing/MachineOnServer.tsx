import Link from "next/link";
import { Arrow, Box, Figure } from "./Diagram";

// The OrderMachine from the Recovery sample, drawn as one move: the page asks, the server checks, and the move is
// stored or refused with the machine's reason. The machine's code is on the linked page and in "Built with Trax".
export default function MachineOnServer() {
  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">The state lives on the server</h2>
        <p className="mt-3 max-w-2xl text-text-secondary">
          A state machine is declared in C#. Its declared rules travel to the page as data, so the page uses the same
          machine to know what it can do next, and the server checks every move against the machine&apos;s rules and the
          caller before storing anything. A refused move comes back with the reason the machine gives. A rule only your
          own code can compute is written once for each runtime that runs it.
        </p>
        <div className="mt-4 flex flex-wrap gap-x-6 gap-y-2 text-sm font-medium">
          <Link href="/docs/statemachine/authoring" className="text-accent hover:text-accent-hover">
            How a machine is written &rarr;
          </Link>
          <Link href="/docs/statemachine#two-runtimes-one-behavior" className="text-accent hover:text-accent-hover">
            What travels and what does not &rarr;
          </Link>
        </div>

        <Figure
          caption={
            <>
              The page never sends a price. The server reads it from its own menu when an item is added, and only the
              outcome of the delivery workflow moves an order past Ordered.
            </>
          }
        >
          <div className="flex flex-col items-stretch gap-3 sm:flex-row sm:items-center">
            <Box label="The page" note="Place order" className="sm:w-40" />
            <Arrow />
            <Box label="The server" note="checks the rules and the caller" className="sm:flex-1" />
            <Arrow />
            <div className="flex flex-col gap-3 sm:w-64">
              <Box tone="accent" label="Stored" note="the order is now Ordered" />
              <Box tone="fail" label="Refused" note="“The cart is empty.”" />
            </div>
          </div>
        </Figure>
      </div>
    </section>
  );
}
