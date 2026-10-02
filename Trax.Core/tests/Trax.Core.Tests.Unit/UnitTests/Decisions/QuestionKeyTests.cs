using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;
using Before = Trax.Core.Tests.Unit.UnitTests.Decisions.Keys.Before;
using Elsewhere = Trax.Core.Tests.Unit.UnitTests.Decisions.Keys.Elsewhere;
using Renamed = Trax.Core.Tests.Unit.UnitTests.Decisions.Keys.Renamed;

namespace Trax.Core.Tests.Unit.UnitTests.Decisions
{
    /// <summary>
    /// The key a question is asked under is short and leaves the namespace out, so a move to
    /// another namespace neither shifts what a model is told nor stops a requeue replaying.
    /// [Asks(Key = ...)] sets one explicitly, and two types one train would ask under one key are
    /// refused rather than having their answers mixed up.
    /// </summary>
    public class QuestionKeyTests : TestSetup
    {
        #region The default key

        [Test]
        public void For_ATopLevelType_IsItsNameWithoutTheNamespace() =>
            QuestionKey.For<Before.RefundChoice>().Should().Be("RefundChoice");

        [Test]
        public void For_ANestedType_IsPrefixedWithTheTypesItIsNestedIn() =>
            QuestionKey.For<Queue.Lane>().Should().Be("QuestionKeyTests.Queue.Lane");

        [Test]
        public void For_AGenericType_WritesItsArgumentsReadably() =>
            QuestionKey
                .For<Flag<Before.RefundChoice, Queue.Lane>>()
                .Should()
                .Be("QuestionKeyTests.Flag<RefundChoice,QuestionKeyTests.Queue.Lane>");

        [Test]
        public void For_ATypeMovedToAnotherNamespace_KeepsItsKey() =>
            QuestionKey
                .For<Renamed.RefundChoice>()
                .Should()
                .Be(QuestionKey.For<Before.RefundChoice>());

        #endregion

        #region The explicit key

        [Test]
        public void For_ATypeThatSetsAKey_IsThatKey() =>
            QuestionKey.For<Keyed>().Should().Be("refund_choice");

        [Test]
        public void For_AGenericArgumentThatSetsAKey_IsWrittenByThatKey() =>
            QuestionKey
                .For<Flag<Keyed, Queue.Lane>>()
                .Should()
                .Be("QuestionKeyTests.Flag<refund_choice,QuestionKeyTests.Queue.Lane>");

        [TestCase("refund_choice", true)]
        [TestCase("Refunds.v2-choice", true)]
        [TestCase("", false)]
        [TestCase("refund choice", false)]
        [TestCase("refund\"choice", false)]
        [TestCase("rückerstattung", false)]
        public void IsValid_TakesOnlyAShortAsciiKey(string key, bool valid) =>
            QuestionKey.IsValid(key).Should().Be(valid);

        [Test]
        public void IsValid_RefusesAKeyLongerThanTheMaximum()
        {
            QuestionKey.IsValid(new string('k', QuestionKey.MaxLength)).Should().BeTrue();
            QuestionKey.IsValid(new string('k', QuestionKey.MaxLength + 1)).Should().BeFalse();
        }

        [Test]
        public async Task Run_TheDeciderIsAskedUnderTheExplicitKey()
        {
            var decider = new ScriptedDecider().Choose(Keyed.Full);

            var result = await new KeyTrain(
                t => t.Decide<string>(q => q.Choice<Keyed>()),
                decider
            ).RunEither("x");

            result.IsRight.Should().BeTrue();
            decider.Requests.Single().Questions.Single().Key.Should().Be("refund_choice");
        }

        [Test]
        public void Verify_AnExplicitKeyThatIsNotValid_IsRefused() =>
            Verify(t => t.Decide<string>(q => q.Choice<BadlyKeyed>()))
                .Should()
                .Contain(f =>
                    f.IsRefusal
                    && f.Reason.Contains("declares the question key 'refund choice'")
                    && f.Reason.Contains($"1 to {QuestionKey.MaxLength} ASCII letters")
                );

        #endregion

        #region Two types under one key

        [Test]
        public void Verify_TwoTypesWithOneKeyInOneDecide_IsRefused() =>
            Verify(t =>
                    t.Decide<string>(q =>
                        q.Choice<Before.RefundChoice>().Choice<Elsewhere.RefundChoice>()
                    )
                )
                .Should()
                .Contain(f =>
                    f.IsRefusal
                    && f.Reason.Contains("under the same question key 'RefundChoice'")
                    && f.Reason.Contains("Key = ")
                );

        [Test]
        public void Verify_TwoTypesWithOneKeyInDifferentSteps_IsRefused() =>
            Verify(t =>
                    t.Decide<string>(q => q.Choice<Before.RefundChoice>())
                        .Decide<string>(q => q.Choice<Elsewhere.RefundChoice>())
                )
                .Should()
                .ContainSingle(f => f.IsRefusal)
                .Which.Reason.Should()
                .Contain("under the same question key 'RefundChoice'");

        [Test]
        public void Verify_TwoTypesWithOneKey_OneInsideATrack_IsRefused() =>
            Verify(t =>
                    t.Decide<string>(q => q.Choice<Before.RefundChoice>())
                        .Switch<string, Queue.Lane>(s =>
                            s.When(
                                    Queue.Lane.Left,
                                    l => l.Decide<string>(q => q.Choice<Elsewhere.RefundChoice>())
                                )
                                .When(Queue.Lane.Right, r => r)
                        )
                )
                .Should()
                .Contain(f =>
                    f.IsRefusal && f.Reason.Contains("under the same question key 'RefundChoice'")
                );

        [Test]
        public void Verify_OneTypeAskedInTwoSteps_IsNotACollision() =>
            Verify(t =>
                    t.Decide<string>(q => q.Choice<Before.RefundChoice>())
                        .Decide<string>(q => q.Choice<Before.RefundChoice>())
                )
                .Should()
                .NotContain(f => f.IsRefusal);

        [Test]
        public void Verify_TwoTypesWithOneName_OneSettingAKeyOfItsOwn_IsNotACollision() =>
            Verify(t =>
                    t.Decide<string>(q =>
                        q.Choice<Before.RefundChoice>().Choice<Elsewhere.Keyed.RefundChoice>()
                    )
                )
                .Should()
                .NotContain(f => f.IsRefusal);

        [Test]
        public async Task Run_TwoTypesWithOneKeyInDifferentSteps_IsRefusedPermanentlyBeforeTheSecondIsAsked()
        {
            var decider = new ScriptedDecider()
                .Choose(Before.RefundChoice.Full)
                .Choose(Elsewhere.RefundChoice.Credit);

            var result = await new KeyTrain(
                t =>
                    t.Decide<string>(q => q.Choice<Before.RefundChoice>())
                        .Decide<string>(q => q.Choice<Elsewhere.RefundChoice>()),
                decider
            ).RunEither("x");

            var failure = result.Swap().ValueUnsafe();
            failure.Message.Should().Contain("under the same question key 'RefundChoice'");
            ((TrainExceptionData)failure.Data["TrainExceptionData"]!)
                .FailureClass.Should()
                .Be(FailureClass.Permanent);
            decider.Requests.Should().ContainSingle("the second question is never asked");
        }

        #endregion

        #region The fingerprint

        [Test]
        public async Task Fingerprint_IsTheSameWhenTheTypesMoveToAnotherNamespace()
        {
            var before = new Observer();
            var after = new Observer();

            await new KeyTrain(
                t => t.Decide<string>(q => q.Choice<Before.RefundChoice>()),
                new ScriptedDecider().Choose(Before.RefundChoice.Full),
                before
            ).RunEither("x");
            await new KeyTrain(
                t => t.Decide<string>(q => q.Choice<Renamed.RefundChoice>()),
                new ScriptedDecider().Choose(Renamed.RefundChoice.Full),
                after
            ).RunEither("x");

            after.Made.Single().Question.Key.Should().Be(before.Made.Single().Question.Key);
            after.Made.Single().Fingerprint.Should().Be(before.Made.Single().Fingerprint);
        }

        #endregion

        #region Fixtures

        private static IReadOnlyList<ChainFault> Verify(
            Func<MonadTask<string, bool>, MonadTask<string, bool>> chain
        ) =>
            ChainVerification.Verify(
                new KeyTrain(chain, new ScriptedDecider()).DeclaredChain(),
                typeof(string),
                typeof(bool)
            );

        [Asks("How should the refund be paid?", Key = "refund_choice")]
        public enum Keyed
        {
            Full,
            Credit,
        }

        [Asks("How should the refund be paid?", Key = "refund choice")]
        public enum BadlyKeyed
        {
            Full,
            Credit,
        }

        public static class Queue
        {
            [Asks("Which lane?")]
            public enum Lane
            {
                Left,
                Right,
            }
        }

        [Asks("Is it flagged?")]
        public sealed class Flag<TFirst, TSecond>;

        private sealed class Observer : IDecisionObserver
        {
            public List<DecisionMade> Made { get; } = [];

            public Task Decided(DecisionMade decision, CancellationToken cancellationToken)
            {
                Made.Add(decision);
                return Task.CompletedTask;
            }

            public Task Routed(TrackRouted routing, CancellationToken cancellationToken) =>
                Task.CompletedTask;
        }

        private sealed class KeyTrain(
            Func<MonadTask<string, bool>, MonadTask<string, bool>> chain,
            IDecider decider,
            IDecisionObserver? observer = null
        ) : Train<string, bool>
        {
            protected override Task<Either<Exception, bool>> Junctions() =>
                chain(
                        AddServices<IDecider, IDecisionObserver>(
                                decider,
                                observer ?? new Observer()
                            )
                            .Chain<Start>()
                    )
                    .Chain<StringToBool>()
                    .Resolve();
        }

        private class Start : Junction<string, string>
        {
            public override Task<string> Run(string input) => Task.FromResult(input);
        }

        private class StringToBool : Junction<string, bool>
        {
            public override Task<bool> Run(string input) => Task.FromResult(true);
        }

        #endregion
    }
}

// The same enum before and after its namespace is renamed, and another of the same name elsewhere.
namespace Trax.Core.Tests.Unit.UnitTests.Decisions.Keys.Before
{
    [Asks("How should the refund be paid?")]
    public enum RefundChoice
    {
        Full,
        Credit,
    }
}

namespace Trax.Core.Tests.Unit.UnitTests.Decisions.Keys.Renamed
{
    [Asks("How should the refund be paid?")]
    public enum RefundChoice
    {
        Full,
        Credit,
    }
}

namespace Trax.Core.Tests.Unit.UnitTests.Decisions.Keys.Elsewhere
{
    [Asks("Which refund does support offer?")]
    public enum RefundChoice
    {
        Full,
        Credit,
    }

    public static class Keyed
    {
        [Asks("Which refund does support offer?", Key = "support_refund")]
        public enum RefundChoice
        {
            Full,
            Credit,
        }
    }
}
