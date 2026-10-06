using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Monad;
using Trax.Core.Tests.Integration.Examples.Decisions;

namespace Trax.Core.Tests.Integration.IntegrationTests;

/// <summary>
/// Trains that decide at run time which of their declared tracks to take, written the way an
/// application would write them: a support desk that triages messages, an underwriter that must
/// not guess, and a moderation queue that asks three questions at once and routes on all of them.
/// </summary>
public class DecisionExampleTests
{
    #region Support desk: a model-like decider, a higher bar for refunds, a person when unsure

    [Test]
    public async Task SupportDesk_ARefundRequest_RefundsAndNotifies()
    {
        var resolution = await new TriageTicket(new KeywordTriageDecider()).Run(
            new SupportRequest("cus_1", "I was charged twice, I want a refund", 42m)
        );

        resolution.Track.Should().Be("Refund");
        resolution.TookFallback.Should().BeFalse();
        resolution.Actions.Should().Equal("refunded 42.00", "emailed cus_1");
    }

    [Test]
    public async Task SupportDesk_AnOutageReport_PagesOnCall()
    {
        var resolution = await new TriageTicket(new KeywordTriageDecider()).Run(
            new SupportRequest("cus_2", "Your API is down, total outage since 9am", null)
        );

        resolution.Track.Should().Be("Escalate");
        resolution.Actions.Should().Equal("opened incident", "paged on-call");
    }

    [Test]
    public async Task SupportDesk_AnAmbiguousMessage_GoesToAPersonInsteadOfBeingGuessedAt()
    {
        var resolution = await new TriageTicket(new KeywordTriageDecider()).Run(
            new SupportRequest("cus_3", "Where is my refund?", 18m)
        );

        resolution.Track.Should().Be("Otherwise");
        resolution.TookFallback.Should().BeTrue();
        resolution.Actions.Should().Equal("queued for a person");
    }

    [Test]
    public async Task SupportDesk_ARefundBelowItsOwnBar_GoesToAPersonEvenAboveTheSwitchsBar()
    {
        // One keyword: confident enough (0.57) for the switch's 0.5, not for a refund's 0.7.
        var observer = new RecordingObserver();
        var decider = new KeywordTriageDecider();
        var train = new TriageTicketObserved(decider, observer);

        var resolution = await train.Run(new SupportRequest("cus_4", "please refund", 9m));

        resolution.Track.Should().Be("Otherwise");
        observer
            .Routings.Should()
            .ContainSingle()
            .Which.FallbackReason.Should()
            .Contain("below the 0.7 its track requires");
    }

    [Test]
    public async Task SupportDesk_TheDeciderIsAskedTheQuestionWithEachTracksPurpose()
    {
        var decider = new ScriptedDecider().Choose(TicketTrack.SelfServe);

        await new TriageTicket(decider).Run(
            new SupportRequest("cus_5", "  How do I export my invoices?  ", null)
        );

        var request = decider.Requests.Should().ContainSingle().Subject;
        request.Train.Should().Be(nameof(TriageTicket));
        ((Ticket)request.State).Message.Should().Be("How do I export my invoices?");

        var question = request.Questions.Should().ContainSingle().Subject;
        question.Instructions.Should().Be("Which team should handle this support ticket?");
        question
            .Should()
            .BeOfType<ChoiceQuestion>()
            .Which.Options.Should()
            .Equal(
                new Criterion("Refund", "The customer wants their money back for an order."),
                new Criterion(
                    "Escalate",
                    "An outage, a security problem or a legal threat. Page whoever is on call."
                ),
                new Criterion("SelfServe", "A how-to question the help centre already answers.")
            );
    }

    [Test]
    public async Task SupportDesk_EveryDecisionAndRoutingIsReportedWithTheRunsId()
    {
        var observer = new RecordingObserver();
        var train = new TriageTicketObserved(new KeywordTriageDecider(), observer);

        await train.Run(new SupportRequest("cus_6", "I want a refund, charged twice", 20m));

        var decided = observer.Decisions.Should().ContainSingle().Subject;
        decided.RunId.Should().Be(train.ExternalId);
        decided.Answer.Model.Should().Be("keyword-triage-1.0");
        decided.Decider.Should().Be(typeof(KeywordTriageDecider));
        decided.Replayed.Should().BeFalse();

        observer
            .Routings.Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new TrackRouted(
                    nameof(TriageTicketObserved),
                    train.ExternalId,
                    typeof(TicketTrack),
                    "Refund",
                    null
                )
            );
    }

    [Test]
    public async Task SupportDesk_AModelOutage_FailsTheRunNamingTheStep()
    {
        var decider = new ScriptedDecider().Throws(
            new HttpRequestException("model endpoint returned 503")
        );

        var result = await new TriageTicket(decider).RunEither(
            new SupportRequest("cus_7", "refund", 5m)
        );

        var failure = result.Swap().ValueUnsafe();
        failure.Should().BeOfType<HttpRequestException>("an error is not a decision");
        failure
            .Data["TrainExceptionData"]
            .Should()
            .BeOfType<TrainExceptionData>()
            .Which.Junction.Should()
            .Be("Switch<Ticket, TicketTrack>", "the run records which step failed");
    }

    [Test]
    public void SupportDesk_TheWholeChainVerifiesFromItsDeclaration()
    {
        var chain = new TriageTicket(new KeywordTriageDecider()).DeclaredChain();

        ChainVerification
            .Verify(chain, typeof(SupportRequest), typeof(Resolution))
            .Should()
            .BeEmpty();

        var routing = chain.Steps.ToList().FindIndex(s => s.Kind == ChainStepKind.Switch);
        chain
            .TracksAt(routing)
            .Select(t => t.Name)
            .Should()
            .Equal("Refund", "Escalate", "SelfServe", "Otherwise");
    }

    #endregion

    #region Lending: written policy as a decider, and no default track to fall into

    [Test]
    public async Task Lending_TheCreditPolicyApprovesAStrongApplication() =>
        (
            await new UnderwriteLoan(CreditPolicy.Decider()).Run(
                new LoanApplication("a1", 780, 0.22m)
            )
        )
            .Outcome.Should()
            .Be("offer made");

    [Test]
    public async Task Lending_TheCreditPolicyDeclinesAWeakApplication() =>
        (await new UnderwriteLoan(CreditPolicy.Decider()).Run(new LoanApplication("a2", 540, 0.3m)))
            .Outcome.Should()
            .Be("declined, notice sent");

    [Test]
    public async Task Lending_AnUnsureApproval_FailsTheRunAndNothingIsDone()
    {
        var decider = new ScriptedDecider().Choose(Underwriting.Approve, confidence: 0.62);

        var result = await new UnderwriteLoan(decider).RunEither(
            new LoanApplication("a3", 700, 0.4m)
        );

        var failure = result.Swap().ValueUnsafe();
        failure
            .Message.Should()
            .Contain("confidence of 0.62")
            .And.Contain("below the 0.95")
            .And.Contain("declares no Otherwise track");
        ((TrainExceptionData)failure.Data["TrainExceptionData"]!)
            .FailureClass.Should()
            .Be(FailureClass.Permanent, "asking the same declaration again gets the same answer");
    }

    [Test]
    public async Task Lending_ManualReviewNeedsNoConfidenceAtAll()
    {
        var decider = new ScriptedDecider().Choose(Underwriting.ManualReview, confidence: 0.1);

        var decision = await new UnderwriteLoan(decider).Run(new LoanApplication("a4", 690, 0.4m));

        decision.Outcome.Should().Be("assigned to an underwriter");
    }

    [Test]
    public void Lending_ADeciderRegisteredOnceInTheContainer_SatisfiesTheStartupCheck()
    {
        var chain = new UnderwriteLoanFromContainer().DeclaredChain();

        using var without = new ServiceCollection().BuildServiceProvider();
        ChainVerification
            .Verify(
                chain,
                typeof(LoanApplication),
                typeof(LoanDecision),
                without.GetRequiredService<IServiceProviderIsService>()
            )
            .Should()
            .ContainSingle()
            .Which.Reason.Should()
            .Contain("needs a decider");

        using var with = new ServiceCollection()
            .AddSingleton<IDecider>(CreditPolicy.Decider())
            .BuildServiceProvider();
        ChainVerification
            .Verify(
                chain,
                typeof(LoanApplication),
                typeof(LoanDecision),
                with.GetRequiredService<IServiceProviderIsService>()
            )
            .Should()
            .BeEmpty();
    }

    #endregion

    #region Moderation: three questions in one call, routed three ways

    [Test]
    public async Task Moderation_AllThreeQuestionsAreAskedInOneCall()
    {
        var decider = new ScriptedDecider()
            .YesNo<ContainsThreat>(0.02)
            .Choose(Verdict.Allow)
            .Score<Severity>(0.1);

        var outcome = await new ModeratePost(decider).Run(new Post("p1", "nice cat"));

        outcome.Action.Should().Be("published");
        decider
            .Requests.Should()
            .ContainSingle()
            .Which.Questions.Select(q => q.Key)
            .Should()
            .Equal(
                QuestionKey.For<ContainsThreat>(),
                QuestionKey.For<Verdict>(),
                QuestionKey.For<Severity>()
            );
    }

    [Test]
    public async Task Moderation_AThreatIsTakenDownWhateverTheVerdict()
    {
        var decider = new ScriptedDecider()
            .YesNo<ContainsThreat>(0.93)
            .Choose(Verdict.Allow)
            .Score<Severity>(0);

        (await new ModeratePost(decider).Run(new Post("p2", "...")))
            .Action.Should()
            .Be("taken down");
    }

    [Test]
    public async Task Moderation_ANearThreat_GoesStraightToTrustAndSafety()
    {
        var decider = new ScriptedDecider()
            .YesNo<ContainsThreat>(0.5)
            .Choose(Verdict.Allow)
            .Score<Severity>(0);

        (await new ModeratePost(decider).Run(new Post("p3", "...")))
            .Action.Should()
            .Be("paged trust and safety", "0.5 is between the No bar (0.3) and the Yes bar (0.7)");
    }

    [TestCase(0.4, "queued for a moderator")]
    [TestCase(1.4, "queued for a moderator")]
    [TestCase(1.6, "paged trust and safety")]
    public async Task Moderation_APostForReview_IsRoutedBySeverity(double severity, string action)
    {
        var decider = new ScriptedDecider()
            .YesNo<ContainsThreat>(0.1)
            .Choose(Verdict.Review)
            .Score<Severity>(severity);

        (await new ModeratePost(decider).Run(new Post("p4", "..."))).Action.Should().Be(action);
    }

    [Test]
    public void Moderation_TheWholeTreeVerifies()
    {
        var chain = new ModeratePost(new ScriptedDecider()).DeclaredChain();

        ChainVerification.Verify(chain, typeof(Post), typeof(ModerationOutcome)).Should().BeEmpty();
        chain
            .Steps.Where(s => s.Kind == ChainStepKind.Decide)
            .Select(s => s.Out)
            .Should()
            .Equal(
                typeof(YesNoDecision<ContainsThreat>),
                typeof(ChoiceDecision<Verdict>),
                typeof(ScoreDecision<Severity>)
            );
    }

    #endregion

    #region Cascade, shadow and replay

    [Test]
    public async Task Cascade_OnlyTheQuestionsTheFastDeciderWasUnsureOf_AreEscalated()
    {
        var fast = new ScriptedDecider()
            .YesNo<ContainsThreat>(0.55)
            .Choose(Verdict.Allow, confidence: 0.97)
            .Score<Severity>(0, confidence: 0.9);
        var slow = new ScriptedDecider().YesNo<ContainsThreat>(0.04);

        var outcome = await new ModeratePost(new CascadingDecider(fast, slow)).Run(
            new Post("p5", "...")
        );

        outcome.Action.Should().Be("published");
        slow.Requests.Should()
            .ContainSingle()
            .Which.Questions.Select(q => q.Key)
            .Should()
            .Equal(QuestionKey.For<ContainsThreat>());
    }

    [Test]
    public async Task Cascade_AFailedEscalation_FailsTheRun()
    {
        var fast = new ScriptedDecider().Choose(Underwriting.Approve, confidence: 0.4);
        var slow = new ScriptedDecider().Throws(new TimeoutException("the large model timed out"));

        var result = await new UnderwriteLoan(new CascadingDecider(fast, slow)).RunEither(
            new LoanApplication("a5", 700, 0.3m)
        );

        result.Swap().ValueUnsafe().Should().BeOfType<TimeoutException>();
    }

    [Test]
    public async Task Shadow_IsAskedEverything_RecordedAndNeverActedOn()
    {
        var live = new ScriptedDecider()
            .YesNo<ContainsThreat>(0.1)
            .Choose(Verdict.Allow)
            .Score<Severity>(0);
        var candidate = new CandidateDecider(
            new ScriptedDecider()
                .YesNo<ContainsThreat>(0.2)
                .Choose(Verdict.Remove)
                .Score<Severity>(0.2)
        );
        var observer = new RecordingObserver();

        var outcome = await new ShadowedModeration(live, candidate, observer).Run(
            new Post("p6", "...")
        );

        outcome.Action.Should().Be("published", "the live decider's answer is the one acted on");
        observer
            .Decisions.Select(d => (d.Question.Key, d.Shadows.Single().Agrees))
            .Should()
            .Equal(
                (QuestionKey.For<ContainsThreat>(), true),
                (QuestionKey.For<Verdict>(), false),
                (QuestionKey.For<Severity>(), true)
            );
    }

    [Test]
    public async Task Shadow_ThatFails_DoesNotFailTheRun()
    {
        var live = new ScriptedDecider()
            .YesNo<ContainsThreat>(0.1)
            .Choose(Verdict.Allow)
            .Score<Severity>(0);
        var candidate = new CandidateDecider(
            new ScriptedDecider().Throws(new InvalidOperationException("shadow is down"))
        );
        var observer = new RecordingObserver();

        var outcome = await new ShadowedModeration(live, candidate, observer).Run(
            new Post("p7", "...")
        );

        outcome.Action.Should().Be("published");
        observer
            .Decisions.SelectMany(d => d.Shadows)
            .Should()
            .OnlyContain(s => !s.Agrees && s.Error!.Contains("shadow is down"));
    }

    [Test]
    public async Task Replay_TakesTheEarlierRunsAnswerWithoutAskingTheDecider()
    {
        var journal = new Journal();
        var application = new LoanApplication("a6", 700, 0.4m);

        (
            await new ReplayedUnderwriting(
                new ScriptedDecider().Choose(Underwriting.Approve, model: "jev-1.13.0"),
                journal,
                journal
            ).Run(application)
        )
            .Outcome.Should()
            .Be("offer made");

        // The requeue asks a decider that would now decline, and is never asked.
        var decider = new ScriptedDecider().Choose(Underwriting.Decline);
        var observer = new RecordingObserver();

        var decision = await new ReplayedUnderwriting(decider, journal, observer).Run(application);

        decision.Outcome.Should().Be("offer made");
        decider.Requests.Should().BeEmpty();
        observer
            .Decisions.Should()
            .ContainSingle()
            .Which.Should()
            .Match<DecisionMade>(d => d.Replayed && d.Answer.Model == "jev-1.13.0");
    }

    #endregion

    #region Helpers

    private sealed class RecordingObserver : IDecisionObserver
    {
        public List<DecisionMade> Decisions { get; } = [];

        public List<TrackRouted> Routings { get; } = [];

        public Task Decided(DecisionMade decision, CancellationToken cancellationToken)
        {
            Decisions.Add(decision);
            return Task.CompletedTask;
        }

        public Task Routed(TrackRouted routing, CancellationToken cancellationToken)
        {
            Routings.Add(routing);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// A second decider contract, so a shadow can be told apart from the live one. AddServices
    /// takes interfaces; from the container a concrete type would do.
    /// </summary>
    private interface ICandidateDecider : IDecider;

    private sealed class CandidateDecider(IDecider inner) : ICandidateDecider
    {
        public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct) =>
            inner.Decide(request, ct);
    }

    /// <summary>
    /// Records every answer with its fingerprint and replays it, as a host's journal does. The
    /// run id is ignored, so a second run stands in for a requeue of the first.
    /// </summary>
    private sealed class Journal : IDecisionObserver, IDecisionReplay
    {
        private readonly Dictionary<(string Key, int Occurrence), RecordedAnswer> _recorded = [];

        public Task Decided(DecisionMade decision, CancellationToken cancellationToken)
        {
            _recorded[(decision.Question.Key, decision.Occurrence)] = new RecordedAnswer(
                decision.Answer,
                decision.Fingerprint
            )
            {
                StateHash = decision.StateHash,
            };
            return Task.CompletedTask;
        }

        public Task Routed(TrackRouted routing, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<RecordedAnswer?> Replay(
            string train,
            string runId,
            string key,
            int occurrence,
            CancellationToken cancellationToken
        ) => Task.FromResult(_recorded.GetValueOrDefault((key, occurrence)));
    }

    private sealed class TriageTicketObserved(IDecider decider, IDecisionObserver observer)
        : Trax.Core.Train.Train<SupportRequest, Resolution>
    {
        protected override Task<Trax.Core.Functional.Either<Exception, Resolution>> Junctions() =>
            AddServices(decider, observer)
                .Chain<ParseTicket>()
                .Switch<Ticket, TicketTrack>(tracks =>
                    tracks
                        .When(
                            TicketTrack.Refund,
                            t => t.Chain<IssueRefund>().Chain<NotifyCustomer>(),
                            requireConfidence: 0.7
                        )
                        .When(TicketTrack.Escalate, t => t.Chain<OpenIncident>())
                        .When(TicketTrack.SelfServe, t => t.Chain<SendHelpArticle>())
                        .RequireConfidence(0.5)
                        .Otherwise(t => t.Chain<QueueForHuman>())
                )
                .Chain<CloseTicket>()
                .Resolve();
    }

    private sealed class ShadowedModeration(
        IDecider decider,
        ICandidateDecider candidate,
        IDecisionObserver observer
    ) : Trax.Core.Train.Train<Post, ModerationOutcome>
    {
        protected override Task<Trax.Core.Functional.Either<
            Exception,
            ModerationOutcome
        >> Junctions() =>
            AddServices(decider, candidate, observer)
                .Decide<Post>(q =>
                    q.YesNo<ContainsThreat>()
                        .Choice<Verdict>()
                        .Score<Severity>()
                        .Shadow<ICandidateDecider>()
                )
                .Gate<ContainsThreat>(gate =>
                    gate.Yes(t => t.Chain<TakeDownPost>())
                        .No(t =>
                            t.Switch<Verdict>(v =>
                                v.When(Verdict.Allow, a => a.Chain<PublishPost>())
                                    .Otherwise(o => o.Chain<QueueForModerator>())
                            )
                        )
                )
                .Resolve();
    }

    private sealed class ReplayedUnderwriting(
        IDecider decider,
        IDecisionReplay replay,
        IDecisionObserver observer
    ) : Trax.Core.Train.Train<LoanApplication, LoanDecision>
    {
        protected override Task<Trax.Core.Functional.Either<Exception, LoanDecision>> Junctions() =>
            AddServices(decider, replay, observer)
                .Switch<LoanApplication, Underwriting>(tracks =>
                    tracks
                        .When(Underwriting.Approve, t => t.Chain<MakeOffer>())
                        .When(Underwriting.Decline, t => t.Chain<SendAdverseActionNotice>())
                        .When(Underwriting.ManualReview, t => t.Chain<AssignUnderwriter>())
                )
                .Resolve();
    }

    #endregion
}
