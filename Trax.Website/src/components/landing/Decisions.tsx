import DemoSection from "./DemoSection";
import RecoveryReplay from "./RecoveryReplay";

// The Recovery sample's refund, replayed from one recorded run: it asks its model two questions and crashes while
// paying, the retry replays both answers instead of asking again and crashes while emailing, and the last retry
// starts from the checkpoint.
export default function Decisions() {
  return (
    <DemoSection
      title="A workflow can choose its path at run time"
      intro={
        <p>
            A workflow lists its paths, and a model or a rule table picks one at run time. Each answer is saved, so a retry
            after a crash takes the same path without asking again, unless the data changed. A checkpoint saves the
            result so far, so a retry after it starts there and does not pay twice.
        </p>
      }
      links={[
        { href: "/docs/core/decisions", label: "How a workflow asks a question" },
        { href: "/docs/effect/decisions", label: "How answers are recorded and replayed" },
      ]}
      source={{
        folder: "Recovery",
        files: [
          { path: "Trax.Samples.Recovery/Trains/Refund/ApproveRefundTrain.cs", label: "the refund: two questions and a checkpoint" },
          { path: "Trax.Samples.Recovery/Trains/Refund/RefundModel.cs", label: "the questions the model is asked" },
          { path: "Trax.Samples.Recovery/Model/DemoDecider.cs", label: "the stand-in decision model" },
          { path: "Trax.Samples.Recovery.Api/Program.cs", label: "the host: decision recording, step events, the scheduler" },
        ],
        docs: "/docs/samples/recovery",
        run: [
          "docker compose up -d database",
          "dotnet run --project samples/Recovery/Trax.Samples.Recovery.Api",
          "cd samples/Recovery/Trax.Samples.Recovery.Client && npm ci && npm run dev",
        ],
      }}
    >
      <RecoveryReplay />
    </DemoSection>
  );
}
