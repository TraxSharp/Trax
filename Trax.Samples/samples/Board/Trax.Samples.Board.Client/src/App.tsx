import { useEffect, useState } from "react";
import { boardPage, type BoardPageResult } from "./trax/index.g";

type Ticket = BoardPageResult["tickets"][number];

const LANES: { key: string; name: string; worker: string }[] = [
  { key: "triage", name: "Triage", worker: "triage-bot" },
  { key: "reproduce", name: "Reproduce", worker: "repro-bot" },
  { key: "fix", name: "Fix", worker: "fix-bot" },
  { key: "qa", name: "QA", worker: "qa-bot" },
  { key: "review", name: "Review", worker: "Sam" },
  { key: "done", name: "Done", worker: "no one" },
];

/** The board as the server holds it, read once a second. Nothing here moves a ticket. */
export function App() {
  const [tickets, setTickets] = useState<Ticket[]>([]);
  const [problem, setProblem] = useState<string | null>(null);

  useEffect(() => {
    let live = true;
    const read = async () => {
      try {
        const answer = await boardPage();
        if (live) {
          setTickets(answer.tickets);
          setProblem(null);
        }
      } catch (error) {
        if (live) setProblem(error instanceof Error ? error.message : "The host could not be reached.");
      }
    };
    void read();
    const timer = setInterval(read, 1000);
    return () => {
      live = false;
      clearInterval(timer);
    };
  }, []);

  return (
    <main>
      <h1>Board</h1>
      <p className="lede">Each lane is worked by its own client. A ticket moves only when the server's check accepts what was sent.</p>
      {problem && <p className="problem">{problem}</p>}
      <div className="lanes">
        {LANES.map((lane) => (
          <section key={lane.key} className="lane">
            <h2>{lane.name}</h2>
            <p className="worker">{lane.worker}</p>
            {tickets
              .filter((t) => t.lane === lane.key)
              .map((t) => (
                <article key={t.id} className="ticket">
                  <header>
                    <span>{t.id}</span>
                    <span className="state">{t.state}</span>
                  </header>
                  <p>{t.title}</p>
                  {t.note && <p className="note">{t.note}</p>}
                  <p className="by">{t.claimedBy ? `claimed by ${t.claimedBy}` : t.by ? `moved by ${t.by}` : "not moved yet"}</p>
                </article>
              ))}
          </section>
        ))}
      </div>
    </main>
  );
}
