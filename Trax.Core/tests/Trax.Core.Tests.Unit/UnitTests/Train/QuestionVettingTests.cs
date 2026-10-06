using AwesomeAssertions;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// A decider that vets questions (<see cref="IVetsQuestions"/>) refuses, when the chain is read,
/// what the declaration alone shows it cannot answer, so the startup check reports it before any
/// run is refused for it.
/// </summary>
public class QuestionVettingTests : TestSetup
{
    [Test]
    public void DeclaredChain_ALiveDeciderThatVetsQuestions_RefusesTheStepForEachProblem()
    {
        var decider = new Vetting("takes at most one option");

        var faults = Verify(
            t =>
                t.Switch<string, Lane>(s =>
                    s.When(Lane.Left, l => l.Chain<StringToBool>())
                        .When(Lane.Right, r => r.Chain<StringToBool>())
                ),
            new Services().With<IDecider>(decider)
        );

        faults
            .Should()
            .ContainSingle(f => f.IsRefusal)
            .Which.Should()
            .Match<ChainFault>(f =>
                f.StepIndex == 2
                && f.Kind == ChainStepKind.Decide
                && f.Junction == typeof(IDecider)
                && f.Reason
                    == "Switch<String, Lane>: the decider 'Vetting' cannot answer it: takes at most "
                        + "one option"
            );
        decider.Asked.Should().Be(0, "a decider is vetted, never asked to decide, while reading");
    }

    [Test]
    public void DeclaredChain_ADeciderThatVetsQuestions_IsShownTheQuestionsAsTheStepAsksThem()
    {
        var decider = new Vetting();

        Verify(
            t => t.Switch<string, Lane>(s => s.When(Lane.Left, l => l.Chain<StringToBool>())),
            new Services().With<IDecider>(decider)
        );

        var declared = decider.Vetted.Should().ContainSingle().Subject;
        declared.Train.Should().Be(nameof(VettedTrain));
        declared.Step.Should().Be("Switch<String, Lane>");
        declared.State.Should().Be(typeof(string));
        declared
            .Questions.Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<ChoiceQuestion>()
            .Which.Options.Select(o => o.Name)
            .Should()
            .Equal(["Left"], "the switch offers only the members it has a track for");
    }

    [Test]
    public void DeclaredChain_AShadowThatVetsQuestions_RefusesTheStep() =>
        Verify(
                t => t.Decide<string>(q => q.YesNo<Flag>().Shadow<IShadow>()).Chain<StringToBool>(),
                new Services()
                    .With<IDecider>(new ScriptedDecider())
                    .With<IShadow>(new ShadowVetting("cannot ask yes or no"))
            )
            .Should()
            .ContainSingle(f => f.IsRefusal)
            .Which.Reason.Should()
            .Be(
                "Decide<String>: the shadow decider 'ShadowVetting' cannot answer it: cannot ask "
                    + "yes or no"
            );

    [Test]
    public void DeclaredChain_ACascade_VetsWithEachOfItsTiers() =>
        Verify(
                t => t.Decide<string>(q => q.YesNo<Flag>()).Chain<StringToBool>(),
                new Services().With<IDecider>(
                    new CascadingDecider(
                        new Vetting("the first tier objects"),
                        new Vetting("the second tier objects")
                    )
                )
            )
            .Where(f => f.IsRefusal)
            .Select(f => f.Reason)
            .Should()
            .Equal(
                "Decide<String>: the decider 'CascadingDecider' cannot answer it: the first tier "
                    + "objects",
                "Decide<String>: the decider 'CascadingDecider' cannot answer it: the second tier "
                    + "objects"
            );

    [Test]
    public void DeclaredChain_ADecisionInsideATrack_IsVettedAndRefusedAtTheRoutingStep() =>
        Verify(
                t =>
                    t.Switch<string, Lane>(s =>
                        s.When(
                            Lane.Left,
                            l => l.Decide<string>(q => q.YesNo<Flag>()).Chain<StringToBool>()
                        )
                    ),
                new Services().With<IDecider>(new YesNoVetting("cannot ask yes or no"))
            )
            .Should()
            .ContainSingle(f => f.IsRefusal)
            .Which.Reason.Should()
            .Contain("track 'Left'")
            .And.Contain("Decide<String>: the decider 'YesNoVetting' cannot answer it");

    [Test]
    public void DeclaredChain_ADeciderTheContainerCannotBuildHere_IsNotVetted()
    {
        var faults = Verify(
            t => t.Decide<string>(q => q.YesNo<Flag>()).Chain<StringToBool>(),
            new Services().Throwing<IDecider>(new InvalidOperationException("needs a request"))
        );

        faults.Should().NotContain(f => f.IsRefusal);
    }

    [Test]
    public void DeclaredChain_ADeciderWhoseVettingThrows_RefusesTheStepSayingSo() =>
        Verify(
                t => t.Decide<string>(q => q.YesNo<Flag>()).Chain<StringToBool>(),
                new Services().With<IDecider>(
                    new Vetting { Throws = new InvalidOperationException("vetting broke") }
                )
            )
            .Should()
            .ContainSingle(f => f.IsRefusal)
            .Which.Reason.Should()
            .EndWith("failed while vetting the questions: vetting broke");

    [Test]
    public void DeclaredChain_ADeciderHandedToAddServices_IsVetted()
    {
        var chain = new HandedTrain(new Vetting("handed over")).DeclaredChain();

        chain.Refusals.Should().ContainSingle().Which.Should().EndWith("handed over");
    }

    private static IReadOnlyList<ChainFault> Verify(
        Func<MonadTask<string, bool>, MonadTask<string, bool>> chain,
        IServiceProvider services
    ) =>
        ChainVerification.Verify(
            new VettedTrain(chain, services).DeclaredChain(),
            typeof(string),
            typeof(bool),
            type =>
            {
                try
                {
                    return services.GetService(type) is not null;
                }
                catch
                {
                    // Registered, but only buildable inside a request.
                    return true;
                }
            }
        );

    [Asks("Which lane?")]
    public enum Lane
    {
        Left,
        Right,
    }

    [Asks("Is the flag up?")]
    public sealed class Flag;

    public interface IShadow : IDecider;

    private sealed class VettedTrain(
        Func<MonadTask<string, bool>, MonadTask<string, bool>> chain,
        IServiceProvider services
    ) : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            chain(AddServices(services).Chain<Start>()).Resolve();
    }

    private sealed class HandedTrain(IDecider decider) : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            AddServices(decider)
                .Decide<string>(q => q.YesNo<Flag>())
                .Chain<StringToBool>()
                .Resolve();
    }

    /// <summary>Names the same problems with every declaration, and notes each one it was shown.</summary>
    private class Vetting(params string[] problems) : IDecider, IVetsQuestions
    {
        public List<DeclaredQuestions> Vetted { get; } = [];

        public int Asked { get; private set; }

        public Exception? Throws { get; init; }

        public IEnumerable<string> Problems(DeclaredQuestions declared)
        {
            if (Throws is not null)
                throw Throws;

            Vetted.Add(declared);
            return problems;
        }

        public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct)
        {
            Asked++;
            return Task.FromResult(new DecisionResult(new Dictionary<string, Answer>()));
        }
    }

    private sealed class ShadowVetting(params string[] problems) : Vetting(problems), IShadow;

    /// <summary>Objects to yes/no questions only, so a track that asks none is let through.</summary>
    private sealed class YesNoVetting(string problem) : IDecider, IVetsQuestions
    {
        public IEnumerable<string> Problems(DeclaredQuestions declared) =>
            declared.Questions.OfType<YesNoQuestion>().Select(_ => problem);

        public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct) =>
            Task.FromResult(new DecisionResult(new Dictionary<string, Answer>()));
    }

    private sealed class Services : IServiceProvider
    {
        private readonly Dictionary<Type, Func<object>> _registered = [];

        public Services With<T>(T service)
            where T : class
        {
            _registered[typeof(T)] = () => service;
            return this;
        }

        public Services Throwing<T>(Exception failure)
        {
            _registered[typeof(T)] = () => throw failure;
            return this;
        }

        public object? GetService(Type serviceType) =>
            _registered.TryGetValue(serviceType, out var make) ? make() : null;
    }

    private class Start : Junction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult(input);
    }

    private class StringToBool : Junction<string, bool>
    {
        public override Task<bool> Run(string input) => Task.FromResult(true);
    }
}
