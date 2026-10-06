import Link from "next/link";
import { codeToHtml } from "shiki";
import CodeTabs, { type CodeTab } from "./CodeTabs";

// Every snippet is copied from the Decisions pages in Trax.Docs
// (core/decisions.md, effect/decisions.md), so it matches what the docs show.

const switchCode = `protected override Task<Either<Exception, Resolution>> Junctions() =>
    Chain<ParseTicket>()
        .Switch<Ticket, TicketTrack>(tracks => tracks
            .When(TicketTrack.Refund, t => t.Chain<IssueRefund>().Chain<NotifyCustomer>(),
                  requireConfidence: 0.7)
            .When(TicketTrack.Escalate, t => t.Chain<OpenIncident>())
            .When(TicketTrack.SelfServe, t => t.Chain<SendHelpArticle>())
            .RequireConfidence(0.5)
            .Otherwise(t => t.Chain<QueueForHuman>()))
        .Chain<CloseTicket>()
        .Resolve();`;

interface RawTab {
  label: string;
  caption: string;
  blocks: { file: string; lang: string; code: string }[];
}

const rawTabs: RawTab[] = [
  {
    label: "Ask once, route twice",
    caption:
      "Decide asks several questions about one state in a single call, and later steps route on the answers without asking again. Gate routes on a yes or no probability, Switch on a choice and Scale on a level. A track is a chain like any other, so it can ask and route again.",
    blocks: [
      {
        file: "ModeratePostTrain.cs",
        lang: "csharp",
        code: `AddServices(decider)
    .Decide<Post>(q => q.YesNo<ContainsThreat>().Choice<Verdict>().Score<Severity>())
    .Gate<ContainsThreat>(gate => gate
        .Yes(t => t.Chain<TakeDownPost>(), atLeast: 0.7)
        .No(t => t.Switch<Verdict>(verdict => verdict
            .When(Verdict.Allow, a => a.Chain<PublishPost>())
            .When(Verdict.Remove, r => r.Chain<TakeDownPost>())
            .When(Verdict.Review, r => r.Scale<Severity>(scale => scale
                .AtLeast(Severity.Low, s => s.Chain<QueueForModerator>())
                .AtLeast(Severity.High, s => s.Chain<PageTrustAndSafety>())))),
            below: 0.3)
        .Unsure(t => t.Chain<PageTrustAndSafety>()))
    .Resolve();`,
      },
    ],
  },
  {
    label: "The question",
    caption:
      "A model judges by the question's words and each option's description, not the type's name. The words go on the type, once. A decision with no question at all is refused when the chain is read.",
    blocks: [
      {
        file: "TicketTrack.cs",
        lang: "csharp",
        code: `[Asks("Which team should handle this support ticket?")]
public enum TicketTrack
{
    [Description("The customer wants their money back for an order.")]
    Refund,

    [Description("An outage, a security problem or a legal threat. Page whoever is on call.")]
    Escalate,

    [Description("A how-to question the help centre already answers.")]
    SelfServe,
}`,
      },
    ],
  },
  {
    label: "Deciders",
    caption:
      "IDecider answers the questions; the train does not know what is behind it. AddNimbleDecider points at a Nimble server you run (Nimble is Bespoke Labs' open-weights decision model), and AddSystemOneDecider at another server that accepts the same request format, such as Jev. RuleDecider is policy written as code, and CascadingDecider asks a fast decider first and a slower one only about what the first was unsure of.",
    blocks: [
      {
        file: "Program.cs",
        lang: "csharp",
        code: `services.AddTrax(trax => trax.AddEffects(effects => effects
    .UsePostgres(connectionString)
    .AddDecisionRecording()
    .AddNimbleDecider(o => o.Endpoint = new Uri("http://localhost:8000/v1/systemone"))));`,
      },
      {
        file: "UnderwritingRules.cs",
        lang: "csharp",
        code: `var policy = new RuleDecider().Choice<LoanApplication, Underwriting>(application =>
    application switch
    {
        { CreditScore: >= 740, DebtToIncome: <= 0.36m } => Underwriting.Approve,
        { CreditScore: < 580 } or { DebtToIncome: > 0.5m } => Underwriting.Decline,
        _ => Underwriting.ManualReview,
    });`,
      },
      {
        file: "Cascade.cs",
        lang: "csharp",
        code: `services.AddSingleton<IDecider>(sp => new CascadingDecider(
    first: sp.GetRequiredService<SystemOneDecider>(),   // Nimble
    then: sp.GetRequiredService<LargeModelDecider>(),
    escalateBelow: 0.8));`,
      },
    ],
  },
  {
    label: "Shadows",
    caption:
      "A shadow is asked every question the live decider is asked, alongside it. Its answers are recorded with whether each agreed, and never acted on: a shadow that fails, disagrees or is slow changes nothing. Each shadow gets its own copy of the state and its own DI scope. Use one to move from a rule table to a model, or from one model version to the next, on real traffic.",
    blocks: [
      {
        file: "ModeratePostTrain.cs",
        lang: "csharp",
        code: `.Decide<Post>(q => q.YesNo<ContainsThreat>().Choice<Verdict>().Shadow<ICandidateDecider>())`,
      },
    ],
  },
];

const decisionRow: { name: string; value: string }[] = [
  { name: "question_key", value: "TicketTrack" },
  { name: "kind", value: "choice" },
  { name: "answer", value: "Refund, confidence 0.86" },
  { name: "model", value: "bespokelabs/Bespoke-Nimble-9B" },
  { name: "decider", value: "SystemOneDecider" },
  { name: "shadows", value: "ICandidateDecider: Refund, agreed" },
  { name: "routes", value: "Refund" },
  { name: "refused", value: "null" },
  { name: "replayed", value: "false" },
  { name: "state_hash", value: "k1:9c41e07b…" },
];

export default async function Decisions() {
  const switchHtml = await codeToHtml(switchCode, {
    lang: "csharp",
    theme: "github-dark-dimmed",
  });

  const tabs: CodeTab[] = await Promise.all(
    rawTabs.map(async (tab) => ({
      label: tab.label,
      caption: tab.caption,
      blocks: await Promise.all(
        tab.blocks.map(async (block) => ({
          file: block.file,
          html: await codeToHtml(block.code, {
            lang: block.lang,
            theme: "github-dark-dimmed",
          }),
        })),
      ),
    })),
  );

  return (
    <section className="border-b border-border py-24">
      <div className="mx-auto max-w-6xl px-6 sm:px-8">
        <h2 className="text-2xl font-semibold text-text-primary">
          A train can choose its track at run time
        </h2>
        <p className="mt-3 max-w-2xl text-text-secondary">
          A <code className="font-mono text-[0.95em]">Switch</code>,{" "}
          <code className="font-mono text-[0.95em]">Gate</code> or{" "}
          <code className="font-mono text-[0.95em]">Scale</code> names every
          track the train can take. At run time a decider answers a typed
          question about a value in Memory, and the train takes the matching
          track. Every track is part of the declaration, so the host checks all
          of them at startup, before the first run.
        </p>
        <p className="mt-3 max-w-2xl text-text-secondary">
          An answer below its track&apos;s confidence bar takes the{" "}
          <code className="font-mono text-[0.95em]">Otherwise</code> track,
          and a switch with no{" "}
          <code className="font-mono text-[0.95em]">Otherwise</code> fails the
          run instead of guessing. An answer that does not fit the question is
          never acted on.
        </p>

        <div
          className="mt-8 overflow-hidden rounded border border-accent/30 [&_pre]:overflow-x-auto [&_pre]:bg-bg-secondary [&_pre]:p-5 [&_pre]:font-mono [&_pre]:text-[13px] [&_pre]:leading-relaxed"
          dangerouslySetInnerHTML={{ __html: switchHtml }}
        />

        <div className="mt-10">
          <CodeTabs tabs={tabs} />
        </div>

        <div className="mt-16 grid gap-10 lg:grid-cols-[minmax(0,2fr)_minmax(0,3fr)] lg:gap-16">
          <div className="space-y-4 text-text-secondary">
            <h3 className="text-lg font-semibold text-text-primary">
              Every decision is recorded, and a requeue replays it
            </h3>
            <p>
              With <code className="font-mono text-[0.95em]">AddDecisionRecording</code>,
              each question a run asks is written to{" "}
              <code className="font-mono text-[0.95em]">trax.decision</code>{" "}
              before the train acts on it: the question, the answer, the model
              and decider that gave it, each shadow&apos;s answer, every track
              taken, and why an answer was refused. A run that crashes after
              deciding still shows what it decided.
            </p>
            <p>
              Re-queueing a run from the dashboard or with the{" "}
              <code className="font-mono text-[0.95em]">requeueExecution</code>{" "}
              mutation takes the same tracks as the original, from the recorded
              answers, instead of asking the model again. An answer is replayed
              only into a state that hashes the same as the one it was given
              about; otherwise the decider is asked afresh, and the row says why.
            </p>
            <div className="flex flex-col gap-2 pt-2 text-sm font-medium">
              <Link
                href="/docs/core/decisions"
                className="text-accent hover:text-accent-hover"
              >
                Decisions: questions, tracks and deciders &rarr;
              </Link>
              <Link
                href="/docs/effect/decisions"
                className="text-accent hover:text-accent-hover"
              >
                Recording, replay and Nimble &rarr;
              </Link>
            </div>
          </div>

          <div className="min-w-0">
            <div className="rounded border border-border/50 bg-bg-secondary">
              <div className="border-b border-border/50 px-4 py-2">
                <span className="font-mono text-xs text-text-muted">
                  trax.decision, one row per question asked
                </span>
              </div>
              <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 p-4 font-mono text-[13px]">
                {decisionRow.map((f) => (
                  <div key={f.name} className="contents">
                    <dt className="text-text-muted">{f.name}</dt>
                    <dd className="min-w-0 text-text-primary [overflow-wrap:anywhere]">
                      {f.value}
                    </dd>
                  </div>
                ))}
              </dl>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
