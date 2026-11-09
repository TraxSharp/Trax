import DemoSection from "./DemoSection";
import OrderReplay from "./OrderReplay";

// The Recovery sample's OrderMachine, replayed from a recorded run: the laptop places the order, the server holds the
// machine and runs its train, and the laptop and the phone are both told each move.
export default function MachineOnServer() {
  return (
    <DemoSection
      title="The server holds the state"
      intro={
        <p>
            Each order is a state machine on the server, which checks every move against its rules and the caller before
            storing anything. The page reads the same rules to know what it can do next, and the laptop and the phone
            are both told each change.
        </p>
      }
      links={[
        { href: "/docs/statemachine/authoring", label: "How a machine is written" },
        { href: "/docs/statemachine#two-runtimes-one-behavior", label: "What travels and what does not" },
      ]}
      source={{
        folder: "Recovery",
        files: [
          { path: "Trax.Samples.Recovery/Machines/OrderMachine.cs", label: "the order: its states, events and timer" },
          { path: "Trax.Samples.Recovery/Trains/Order/DeliverOrderTrain.cs", label: "the delivery the order runs" },
          { path: "Trax.Samples.Recovery.Api/Program.cs", label: "the host: machines, step events and pushes" },
        ],
        docs: "/docs/samples/recovery",
        run: [
          "docker compose up -d database",
          "dotnet run --project samples/Recovery/Trax.Samples.Recovery.Api",
          "cd samples/Recovery/Trax.Samples.Recovery.Client && npm ci && npm run dev",
        ],
      }}
    >
      <OrderReplay />
    </DemoSection>
  );
}
