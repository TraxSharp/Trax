import BoardReplay from "./BoardReplay";
import DemoSection from "./DemoSection";
import boardData from "@/data/board-recordings.json";
import type { BoardRecordings } from "@/lib/board-replay";

const recordings = boardData as unknown as BoardRecordings;

// The Board sample, replayed from one recorded scenario: an agent tries to take a ticket out of turn and to approve its
// own fix, and the server refuses both.
export default function AgentsOnTheBoard() {
  return (
    <DemoSection
      title="Agents are checked like anyone else"
      intro={
        <p>
            Agents never move a ticket: they send evidence, and the server checks it itself. Here an agent tries to skip
            ahead and to approve its own fix, and the server refuses both.
        </p>
      }
      links={[{ href: "/docs/samples/board", label: "Read the sample" }]}
      source={{
        folder: "Board",
        files: [
          { path: "Trax.Samples.Board/Machines/AssignmentMachine.cs", label: "an agent's assignment: its events and the checks they run" },
          { path: "Trax.Samples.Board/Machines/TicketMachine.cs", label: "the board: lanes only the server moves" },
          { path: "Trax.Samples.Board/Trains/Checks/ReproduceTrain.cs", label: "a check: the server runs the agent's case itself" },
          { path: "Trax.Samples.Board.Agents/src/scenarios.ts", label: "the scripted agents, on the generated client" },
        ],
        docs: "/docs/samples/board",
        run: [
          "docker compose up -d database",
          "dotnet run --project samples/Board/Trax.Samples.Board.Api",
          "cd samples/Board/Trax.Samples.Board.Agents && npm ci && npm run scenario",
        ],
      }}
    >
      <BoardReplay recordings={recordings} scenarioKey="skip-ahead" />
    </DemoSection>
  );
}
