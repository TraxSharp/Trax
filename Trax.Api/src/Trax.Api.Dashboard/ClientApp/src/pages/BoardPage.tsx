import { useMemo } from "react";
import { Link, Navigate, useParams } from "react-router-dom";
import { Loading } from "../components/detail";
import { MachineBoard } from "../components/MachineBoard";
import { TermHelp } from "../components/TermHelp";
import { useTraxQuery } from "../lib/consoleClient";
import { boardPath, machinePath } from "../lib/machineInstances";
import { machineBoardDocument, machinesDocument } from "../trax/index.g";

const FEATURES_DOCS = "https://traxsharp.net/docs/statemachine/write-machines-as-features";

/**
 * The board, at /board and /board/:machine: every machine this host registers on the left, and the chosen one's
 * event-storming board, the way the team that wrote its features would draw it. Only a machine served from features
 * has one; the others are listed, and say so.
 */
export function BoardPage() {
  const chosen = useParams().machine ?? null;
  const [machines] = useTraxQuery(machinesDocument);
  const defined = machines.data?.operations?.machines;
  const list = defined?.map((m) => m.name) ?? [];
  // Where each machine starts, so a lane that comes round again still reads from its first state.
  const initialStates = useMemo(
    () => Object.fromEntries((defined ?? []).flatMap((m) => (m.initialState ? [[m.name, m.initialState]] : []))),
    [defined],
  );

  return (
    <div>
      <div className="flex items-center gap-2">
        <h1 className="text-2xl font-bold text-fg mb-1">Board</h1>
        <TermHelp term="machine" />
      </div>
      <p className="text-sm text-muted mb-6 max-w-3xl">
        What each machine does, as an event-storming board: who acts, what they ask for, what happens, the rules in
        between and what people read. It is drawn from the features the machine is written in, so it is the same story
        the team wrote, and every note lists the scenarios behind it.
      </p>

      {machines.error ? (
        <div className="bg-danger-soft border border-danger-line text-danger-fg rounded-lg p-4 text-sm">
          {machines.error.message}
        </div>
      ) : !machines.data ? (
        <Loading />
      ) : list.length === 0 ? (
        <p className="text-muted">This host registers no machines.</p>
      ) : (
        <div className="flex flex-col lg:flex-row gap-6 items-start">
          <nav aria-label="Machines" className="w-full lg:w-56 shrink-0 bg-surface rounded-lg border border-line p-2">
            <ul className="space-y-0.5">
              {list.map((name) => (
                <MachineChoice key={name} name={name} current={name === chosen} />
              ))}
            </ul>
          </nav>
          <div className="flex-1 min-w-0 w-full">
            {chosen == null ? (
              <FirstBoard machines={list} />
            ) : !list.includes(chosen) ? (
              <p className="text-muted">This host registers no machine named {chosen}.</p>
            ) : (
              <ChosenBoard machine={chosen} initialStates={initialStates} />
            )}
          </div>
        </div>
      )}
    </div>
  );
}

/** One machine in the list, marked when it has no board. */
function MachineChoice({ name, current }: { name: string; current: boolean }) {
  const [result] = useTraxQuery(machineBoardDocument, { variables: { machine: name } });
  const none = result.data != null && result.data.operations?.machineBoard == null;
  return (
    <li>
      <Link
        to={boardPath(name)}
        aria-current={current ? "page" : undefined}
        className={`flex items-center justify-between gap-2 rounded-md px-3 py-1.5 text-sm ${
          current ? "bg-accent-soft text-accent-fg font-medium" : "text-fg-2 hover:bg-hover"
        }`}
      >
        <span className="truncate">{name}</span>
        {none && <span className="text-[11px] text-muted">no board</span>}
      </Link>
    </li>
  );
}

/**
 * With no machine in the URL, the first one with a board, so the page opens on something to read: each machine's
 * board is asked for in turn until one has one.
 */
function FirstBoard({ machines, at = 0 }: { machines: string[]; at?: number }) {
  const machine = machines[at];
  const [result] = useTraxQuery(machineBoardDocument, { variables: { machine } });
  if (result.error) return <p className="text-danger-fg text-sm">{result.error.message}</p>;
  if (!result.data) return <Loading />;
  if (result.data.operations?.machineBoard != null) return <Navigate to={boardPath(machine)} replace />;
  if (at + 1 < machines.length) return <FirstBoard machines={machines} at={at + 1} />;
  return (
    <p className="text-muted">
      None of this host's machines is served from features, so there is no board to draw.{" "}
      <a href={FEATURES_DOCS} className="text-accent-fg hover:underline">
        A machine written as features
      </a>{" "}
      has one.
    </p>
  );
}

function ChosenBoard({ machine, initialStates }: { machine: string; initialStates: Record<string, string> }) {
  const [result] = useTraxQuery(machineBoardDocument, { variables: { machine } });
  const board = result.data?.operations?.machineBoard ?? null;
  return (
    <section aria-label={`Board of ${machine}`} className="bg-surface rounded-lg border border-line p-5">
      <div className="flex items-baseline justify-between gap-4 mb-3">
        <h2 className="text-lg font-semibold text-fg">{machine}</h2>
        <Link to={machinePath(machine)} className="text-sm text-accent-fg hover:underline">
          States and instances
        </Link>
      </div>
      {result.error ? (
        <p className="text-danger-fg text-sm">{result.error.message}</p>
      ) : !result.data ? (
        <Loading />
      ) : board ? (
        <MachineBoard board={board} initialStates={initialStates} />
      ) : (
        <p className="text-sm text-muted">
          {machine} is not served from features, so it has no board. Its states are on its own page.
        </p>
      )}
    </section>
  );
}
