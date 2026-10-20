---
name: trax-write-a-decider
description: Use when a Trax train has to choose between paths, branch, route, triage, approve or decline, or when a business rule in a train changes often or may move to a model. Covers when a choice belongs in a decider rather than an if in a junction, Switch, Gate, Scale and Decide, writing an IDecider or RuleDecider, confidence thresholds and Otherwise tracks, checkpoints and Parallel branches, running a new decider in shadow, decision replay on requeue and retry, and testing decisions with ScriptedDecider.
metadata:
  internal: true
---

# Write a decider

A train that does different work on different runs declares every track it can take in its
chain, and a **decider** picks one per run by answering a typed question about a value in
Memory. Every track is part of the declaration, so the startup check verifies all of them, and
each decision is recorded and can be replayed. [Decisions](https://traxsharp.net/docs/core/decisions)
explains the model; this skill is when to use it, the steps and the traps. Load
`trax-write-a-train` too if the train does not exist yet.

## Decide first: decider or `if`

| The choice | Where it goes |
|---|---|
| Changes which junctions run (refund or escalate, approve or review) | Tracks on a `Switch`, `Gate` or `Scale`, and a decider |
| A rule that changes often, or that a model may take over | A decider (`RuleDecider` now), so the rule is one place and can be swapped in shadow |
| Only computes one junction's own output, and every run takes the same steps after it | An `if` inside that junction is fine |
| Picks a whole train that stands on its own | A junction that dispatches it through `ITrainBus` is fine |

Never branch in `Junctions()` itself: `TrainInput` throws `ChainDeclarationException` while the
chain is declared. And do not hide a choice of path in a junction that calls one service or
another: the startup check cannot see those paths and nothing records why a run took one.

## Pick the question

| Question | Declare | Routed by |
|---|---|---|
| Which of these options? | an enum | `Switch<TState, TTrack>` |
| Where on an ordered scale? | an enum, lowest to highest | `Scale<TState, TLevel>` |
| How likely is yes? | a marker class | `Gate<TState, TQuestion>` |

A model judges by the words, so put them on the type with `[Asks]` and `[Description]`. A step
can ask its own question, as below, or one `Decide<TState>(q => q.Choice<A>().YesNo<B>())` can
ask several in one call and later `Switch<A>`, `Gate<B>` route on the answers
([asking first](https://traxsharp.net/docs/core/decisions#asking-inline-or-asking-first)).

## Steps

1. **Declare the question and the tracks.** All types are in `Trax.Core.Decisions`. Every track
   must end with whatever the steps after the switch need; here each produces the output.

   ```csharp compile=loan
   using System.ComponentModel;
   using Trax.Core.Decisions;

   namespace Lending.Trains.UnderwriteLoan;

   public record LoanApplication(string ApplicantId, int CreditScore, decimal DebtToIncome);

   public record LoanDecision(string Outcome);

   [Asks("How should this loan application be underwritten?")]
   public enum Underwriting
   {
       [Description("Strong credit and low debt. Make an offer.")]
       Approve,

       [Description("Fails the credit policy. Decline with the required notice.")]
       Decline,

       [Description("Borderline. A human underwriter reviews it.")]
       ManualReview,
   }
   ```

   ```csharp compile=loan
   using Lending.Trains.UnderwriteLoan.Junctions;
   using Trax.Core.Functional;
   using Trax.Effect.Services.ServiceTrain;

   namespace Lending.Trains.UnderwriteLoan;

   public interface IUnderwriteLoanTrain : IServiceTrain<LoanApplication, LoanDecision>;

   public class UnderwriteLoanTrain
       : ServiceTrain<LoanApplication, LoanDecision>,
           IUnderwriteLoanTrain
   {
       protected override Task<Either<Exception, LoanDecision>> Junctions() =>
           Switch<LoanApplication, Underwriting>(tracks =>
                   tracks
                       .When(
                           Underwriting.Approve,
                           t => t.Chain<MakeOfferJunction>(),
                           requireConfidence: 0.95
                       )
                       .When(
                           Underwriting.Decline,
                           t => t.Chain<SendNoticeJunction>(),
                           requireConfidence: 0.95
                       )
                       .When(Underwriting.ManualReview, t => t.Chain<AssignUnderwriterJunction>())
               )
               .Resolve();
   }
   ```

   ```csharp compile=loan
   using Trax.Effect.Services.EffectJunction;

   namespace Lending.Trains.UnderwriteLoan.Junctions;

   // One file each in Junctions/, as for any train. Real ones do the work of their track.
   internal class MakeOfferJunction : EffectJunction<LoanApplication, LoanDecision>
   {
       public override Task<LoanDecision> Run(LoanApplication input) => Task.FromResult(new LoanDecision("offer"));
   }

   internal class SendNoticeJunction : EffectJunction<LoanApplication, LoanDecision>
   {
       public override Task<LoanDecision> Run(LoanApplication input) => Task.FromResult(new LoanDecision("declined"));
   }

   internal class AssignUnderwriterJunction : EffectJunction<LoanApplication, LoanDecision>
   {
       public override Task<LoanDecision> Run(LoanApplication input) => Task.FromResult(new LoanDecision("review"));
   }
   ```

2. **Set the bars per track.** A choice below its track's `requireConfidence` goes to
   `Otherwise`, or fails the run when there is none. Give the track with the gravest consequence
   the highest bar. Leave `Otherwise` out when every outcome has consequences and guessing is
   worse than failing. Adding a track moves every confidence on that switch, so recheck the bars
   ([when the answer is not followed](https://traxsharp.net/docs/core/decisions#when-the-answer-is-not-followed)).

3. **Register a decider.** One `IDecider` in the container serves every decision. Policy is a
   `RuleDecider`, which is always certain; a model is `AddNimbleDecider`; your own implements
   `IDecider` ([Write a decider](https://traxsharp.net/docs/core/write-a-decider)). Add
   `AddDecisionRecording()` after the data provider so decisions are recorded and replayed. A
   host with `AddScheduler`, `AddTraxJobRunner` or `AddTraxWorker` refuses to start without it
   when any of its trains asks a decider:

   ```csharp compile=loan
   using Lending.Trains.UnderwriteLoan;
   using Trax.Core.Decisions;
   using Trax.Effect.Data.Extensions;
   using Trax.Effect.Data.Postgres.Extensions;
   using Trax.Effect.Extensions;
   using Trax.Mediator.Extensions;

   services.AddSingleton<IDecider>(
       new RuleDecider().Choice<LoanApplication, Underwriting>(application =>
           application switch
           {
               { CreditScore: >= 740, DebtToIncome: <= 0.36m } => Underwriting.Approve,
               { CreditScore: < 580 } or { DebtToIncome: > 0.5m } => Underwriting.Decline,
               _ => Underwriting.ManualReview,
           }
       )
   );

   services.AddTrax(trax =>
       trax.AddEffects(effects => effects.UsePostgres(connectionString).AddDecisionRecording())
           .AddMediator(typeof(UnderwriteLoanTrain).Assembly)
   );
   ```

4. **Test each track** (below), then start the host. A fault inside a track is reported as
   `track 'Approve', step 1: ...`.

## A new decider in shadow

To move from rules to a model, or between model versions, run the candidate as a shadow: it is
asked every question beside the live decider, its answers are recorded with whether they agreed,
and it never changes a run. Give it its own type (`public interface ICandidateDecider : IDecider;`),
register it, and add `.Shadow<ICandidateDecider>()` to the `Decide` questions, or to the tracks of
a step that asks its own question. Keep `WaitForShadows` short (default five seconds; every run can
wait that long). Switch over by registering it as the live `IDecider`.
[Try a decider in shadow](https://traxsharp.net/docs/core/try-a-decider-in-shadow).

## Replay

With `AddDecisionRecording()`, a requeued run (`requeueExecution`, the console's Re-queue), a
manifest's retry and a dead letter's requeue replay the recorded answers instead of asking again,
so the repeat takes the tracks the original took. `queueTrain` and a direct run never replay. An
answer is replayed only when the state hashes the same and the answer is younger than
`ReplayAnswersFor` (24 hours). So:

- **Answer from the state alone.** The hash covers the state's value, not what a decider looks up
  elsewhere. A state that carries a timestamp or a generated id never replays.
- **Renaming the enum or marker type changes the question key**; keep the old one with
  `[Asks("...", Key = "OldName")]`. Rewording the question or its options asks afresh too.
- Register a `StateHashKey` (32 bytes or more, the same on every host that may repeat a run).
- Register your own `IDecisionObserver` before `AddTrax`; one registered after it, with decision
  recording on, makes the host refuse to start.

[Decision replay and state hash rules](https://traxsharp.net/docs/core/decision-replay) and
[Recover a deciding train](https://traxsharp.net/docs/effect/recover-a-deciding-train).

## Checkpoints and branches

`Checkpoint<TState>()` stores one value from Memory, with the tracks taken before it, so a retry,
a dead letter's requeue or `resumeExecution` resumes after it (`requeueExecution` reruns from the
top). `TState` is a sealed record of ids and small values, never rows or services. Commit before
the checkpoint: it refuses to write while the data context has unsaved changes. Steps after it run
again on every resume, so they must tolerate running twice. `Parallel` branches each get their own
scope and transaction: branches compute and return values, and the step after the join writes.
[Checkpoints and branches](https://traxsharp.net/docs/core/checkpoints-and-branches).

## Known mistakes

| Wrong | Right |
|---|---|
| `if` over the input in `Junctions()` | A `Switch`, `Gate` or `Scale` with a decider |
| `t => Chain<MakeOfferJunction>()` inside a track | `t => t.Chain<MakeOfferJunction>()`: a track is written on the parameter it is handed |
| A junction after the switch that needs a value only one track produces | Make every track produce it; the check refuses the chain otherwise |
| `.Shadow<T>()` on a `Switch<TTrack>` that routes an earlier `Decide` | Put the shadow on the `Decide`; a step that asks nothing refuses it |
| A decider that changes `request.State`, or reads the database to answer | Read the state and leave it; put what it needs into the state |
| Expecting `Otherwise` to catch a decider that throws or answers badly | Both fail the run whatever fallbacks exist: a bad answer as `Transient`, a declaration fault as `Permanent`. `Otherwise` only takes low confidence and members with no track |

## Testing

`ScriptedDecider` answers what the test says and keeps every request in `Requests`:
`new ScriptedDecider().Choose(Underwriting.Approve, confidence: 0.62)`, registered as the
`IDecider`, sends a run below Approve's bar, which fails it here since there is no `Otherwise`.
To check every track, verify the declared chain:
`ChainVerification.Verify(train.DeclaredChain(), typeof(LoanApplication), typeof(LoanDecision))`
(`Trax.Core.Monad`) returns no faults. See
[Test a train's decisions](https://traxsharp.net/docs/core/test-train-decisions) and `trax-test-your-app`.

## Pages

[Switch](https://traxsharp.net/docs/sdk-reference/train-methods/switch),
[Gate](https://traxsharp.net/docs/sdk-reference/train-methods/gate),
[Scale](https://traxsharp.net/docs/sdk-reference/train-methods/scale),
[Decide](https://traxsharp.net/docs/sdk-reference/train-methods/decide),
[Checkpoint](https://traxsharp.net/docs/sdk-reference/train-methods/checkpoint),
[Parallel](https://traxsharp.net/docs/sdk-reference/train-methods/parallel),
[AddDecisionRecording](https://traxsharp.net/docs/sdk-reference/configuration/add-decision-recording).
What is stored, and replay in full: [Decision records](https://traxsharp.net/docs/effect/decisions).
