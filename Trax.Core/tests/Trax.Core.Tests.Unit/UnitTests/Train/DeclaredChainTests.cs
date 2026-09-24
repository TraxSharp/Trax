using System.Runtime.CompilerServices;
using FluentAssertions;
using LanguageExt;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// Reading a train's chain without running it.
///
/// <para>A chain is a declaration: a sequence of junction types. Reading it answers every chain
/// call by recording type arguments, so the whole chain can be read for every registered train
/// at host startup without resolving a junction from the container or performing any of the
/// work the chain describes.</para>
///
/// <para>Enforces Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md")]
public class DeclaredChainTests : TestSetup
{
    [Test]
    public void DeclaredChain_RecordsEveryStepInOrder()
    {
        var steps = new ThreeStepTrain().DeclaredChain().Steps;

        steps
            .Select(s => (s.Kind, s.Junction))
            .Should()
            .Equal(
                (ChainStepKind.Chain, typeof(StringLength)),
                (ChainStepKind.Chain, typeof(IntToBool)),
                (ChainStepKind.Resolve, null)
            );
    }

    [Test]
    public void DeclaredChain_CarriesTheInputAndOutputTypeOfEachJunction()
    {
        var steps = new ThreeStepTrain().DeclaredChain().Steps;

        steps[0].In.Should().Be(typeof(string));
        steps[0].Out.Should().Be(typeof(int));
        steps[1].In.Should().Be(typeof(int));
        steps[1].Out.Should().Be(typeof(bool));
        steps[2].Out.Should().Be(typeof(bool), "the terminal step names the train's return type");
    }

    [Test]
    public void DeclaredChain_RunsNoJunction()
    {
        CountingJunction.Runs = 0;

        new CountingTrain().DeclaredChain();

        CountingJunction
            .Runs.Should()
            .Be(0, "reading a chain must not perform the work the chain describes");
    }

    [Test]
    public void DeclaredChain_DoesNotNeedTheJunctionToBeConstructible()
    {
        var steps = new UnconstructibleTrain().DeclaredChain().Steps;

        steps
            .Should()
            .Contain(
                s => s.Junction == typeof(NeedsADependency),
                "a chain is read from types alone, so a junction that cannot be constructed here "
                    + "is still declared. Whether it resolves is a separate check the host makes "
                    + "against the container."
            );
    }

    [Test]
    public async Task DeclaredChain_LeavesTheTrainRunnable()
    {
        var train = new ThreeStepTrain();

        train.DeclaredChain();

        (await train.Run("hello"))
            .Should()
            .Be(true, "reading a chain must not leave the train in a declaring state");
    }

    [Test]
    public void DeclaredChain_ShortCircuitAndExtract_AreRecordedToo()
    {
        var steps = new MixedTrain().DeclaredChain().Steps;

        steps
            .Select(s => s.Kind)
            .Should()
            .Equal(ChainStepKind.ShortCircuit, ChainStepKind.Extract, ChainStepKind.Resolve);
    }

    [Test]
    public void DeclaredChain_TheExplicitTypedOverloads_RecordTheirTypeArguments()
    {
        var steps = new ExplicitTypedTrain().DeclaredChain().Steps;

        steps
            .Should()
            .Equal(
                new ChainStep(
                    ChainStepKind.Chain,
                    typeof(StringLength),
                    typeof(string),
                    typeof(int)
                ),
                new ChainStep(ChainStepKind.Chain, typeof(IntToBool), typeof(int), typeof(bool)),
                new ChainStep(ChainStepKind.Resolve, null, null, typeof(bool))
            );
    }

    [Test]
    public void DeclaredChain_AddServices_IsRecordedAsASeedUnderThePassedType()
    {
        var steps = new SeedingTrain().DeclaredChain().Steps;

        steps[0].Should().Be(new ChainStep(ChainStepKind.Seed, null, null, typeof(ICounter)));
        steps[1].Should().Be(new ChainStep(ChainStepKind.Seed, null, null, typeof(int)));
    }

    [Test]
    public void DeclaredChain_AnAsyncBodyThatDoesNotAwaitFirst_IsAnOrdinaryDeclaration()
    {
        var chain = new AsyncWrapperTrain().DeclaredChain();

        chain
            .Refusals.Should()
            .BeEmpty("an async body that awaits only the chain completes at once");
        chain.Steps.Should().HaveCount(3);
    }

    [Test]
    public void DeclaredChain_ABodyThatAwaitsBeforeReturning_IsRefused()
    {
        var chain = new AwaitingTrain().DeclaredChain();

        chain.Refusals.Should().ContainSingle().Which.Should().Contain("awaited something");
    }

    [Test]
    public void DeclaredChain_ABodyThatReturnsAResult_IsRefused()
    {
        var chain = new DirectReturnTrain().DeclaredChain();

        chain.Steps.Should().BeEmpty();
        chain.Refusals.Should().ContainSingle().Which.Should().Contain("returned a result");
    }

    [Test]
    public void DeclaredChain_EndingWithResolveOfAValue_IsRefused()
    {
        var chain = new ValueResolvingTrain().DeclaredChain();

        chain.Refusals.Should().ContainSingle().Which.Should().Contain("Resolve(value)");
    }

    [Test]
    public void DeclaredChain_AnAsyncBodyThatThrows_Rethrows()
    {
        var act = () => new ThrowingAsyncTrain().DeclaredChain();

        act.Should()
            .Throw<InvalidOperationException>(
                "an async body's exception lands in its task, and a clean result would hide it"
            )
            .WithMessage("declaration failed");
    }

    [Test]
    public void DeclaredChain_IChainOfAClass_IsRefused() =>
        new ClassIChainTrain()
            .DeclaredChain()
            .Refusals.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("names a class");

    [Test]
    public void DeclaredChain_AddServicesOfAClass_IsRefused() =>
        new ClassServiceTrain()
            .DeclaredChain()
            .Refusals.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("names a class");

    [Test]
    public void DeclaredChain_AChainBuiltOnAFreshMonad_IsRecordedNotRun()
    {
        CountingJunction.Runs = 0;

        var chain = new FreshMonadTrain().DeclaredChain();

        CountingJunction.Runs.Should().Be(0, "reading a chain must never run a junction");
        chain.Steps.Select(s => s.Kind).Should().Equal(ChainStepKind.Chain, ChainStepKind.Resolve);
    }

    [Test]
    public void DeclaredChain_ATypeThatIsNotAJunction_IsRefusedNotThrown()
    {
        var read = () => new NotAJunctionTrain().DeclaredChain();

        read.Should().NotThrow();
        read().Refusals.Should().ContainSingle().Which.Should().Contain("does not implement");
    }

    [Test]
    public async Task DeclaredChain_AJunctionPassedAsAnInstance_IsRecordedByItsTypeAndNotRun()
    {
        CountingJunction.Runs = 0;
        var train = new InstanceTrain();

        var chain = train.DeclaredChain();

        CountingJunction.Runs.Should().Be(0, "handing over an instance must not run it either");
        chain
            .Steps.Should()
            .Equal(
                new ChainStep(
                    ChainStepKind.ShortCircuit,
                    typeof(CountingJunction),
                    typeof(string),
                    typeof(int)
                ),
                new ChainStep(
                    ChainStepKind.Chain,
                    typeof(CountingJunction),
                    typeof(string),
                    typeof(int)
                ),
                new ChainStep(ChainStepKind.Resolve, null, null, typeof(int))
            );

        (await train.Run("hello"))
            .Should()
            .Be(5, "the instances the declaration named are the ones the run executes");
    }

    [Test]
    public async Task DeclaredChain_TheUnitOutputOverloads_RecordUnitAsTheOutput()
    {
        var expected = new[]
        {
            new ChainStep(
                ChainStepKind.Chain,
                typeof(StringToUnit),
                typeof(string),
                typeof(LanguageExt.Unit)
            ),
            new ChainStep(
                ChainStepKind.Chain,
                typeof(StringToUnit),
                typeof(string),
                typeof(LanguageExt.Unit)
            ),
            new ChainStep(ChainStepKind.Resolve, null, null, typeof(LanguageExt.Unit)),
        };

        new UnitInstanceFirstTrain().DeclaredChain().Steps.Should().Equal(expected);
        new UnitParameterlessFirstTrain().DeclaredChain().Steps.Should().Equal(expected);

        (await new UnitInstanceFirstTrain().RunEither("hello")).IsRight.Should().BeTrue();
        (await new UnitParameterlessFirstTrain().RunEither("hello")).IsRight.Should().BeTrue();
    }

    [Test]
    public void DeclaredChain_ABodyThatFailsAfterAwaiting_LeavesNoUnobservedException()
    {
        var gate = new TaskCompletionSource();
        var unobserved = new List<Exception>();

        void OnUnobserved(object? sender, UnobservedTaskExceptionEventArgs e) =>
            unobserved.AddRange(e.Exception.InnerExceptions);

        TaskScheduler.UnobservedTaskException += OnUnobserved;

        try
        {
            ReadThenFail(gate).Should().ContainSingle().Which.Should().Contain("awaited something");

            // The refused body's task is unreferenced now. Collecting it raises the event for a
            // fault nobody observed.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= OnUnobserved;
        }

        unobserved
            .Should()
            .NotContain(
                e => e.Message == GatedFailingTrain.Failure,
                "reading the chain started this body, so reading it has to observe the outcome"
            );
    }

    /// <summary>
    /// Reads the chain and lets its body fail, in a frame of its own so nothing on the test's
    /// stack keeps the body's task alive.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static IReadOnlyList<string> ReadThenFail(TaskCompletionSource gate)
    {
        var refusals = new GatedFailingTrain(gate.Task).DeclaredChain().Refusals;

        // The body resumes inline here and faults after DeclaredChain has already returned.
        gate.SetResult();

        return refusals;
    }

    private class StringLength : Junction<string, int>
    {
        public override Task<int> Run(string input) => Task.FromResult(input.Length);
    }

    private class IntToBool : Junction<int, bool>
    {
        public override Task<bool> Run(int input) => Task.FromResult(input > 0);
    }

    private class CountingJunction : Junction<string, int>
    {
        public static int Runs;

        public override Task<int> Run(string input)
        {
            Runs++;
            return Task.FromResult(input.Length);
        }
    }

    private class NeedsADependency(string required) : Junction<string, int>
    {
        public override Task<int> Run(string input) => Task.FromResult(required.Length);
    }

    private record Wrapper(string Value);

    private class ThreeStepTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<StringLength>().Chain<IntToBool>().Resolve();
    }

    private class CountingTrain : Train<string, int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<CountingJunction>().Resolve();
    }

    private class UnconstructibleTrain : Train<string, int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<NeedsADependency>().Resolve();
    }

    private class MixedTrain : Train<Wrapper, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            ShortCircuit<WrapperPassthrough>().Extract<Wrapper, string>().Resolve();
    }

    private class WrapperPassthrough : Junction<Wrapper, Wrapper>
    {
        public override Task<Wrapper> Run(Wrapper input) => Task.FromResult(input);
    }

    private interface ICounter;

    private class Counter : ICounter;

    private record Holder(int Count);

    private class ExplicitTypedTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<StringLength, string, int>().Chain<IntToBool, int, bool>().Resolve();
    }

    private class SeedingTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            AddServices<ICounter>(new Counter())
                .Extract<Holder, int>(new Holder(3))
                .Chain<IntToBool>()
                .Resolve();
    }

    private class AsyncWrapperTrain : Train<string, bool>
    {
        protected override async Task<Either<Exception, bool>> Junctions() =>
            await Chain<StringLength>().Chain<IntToBool>().Resolve();
    }

    private class AwaitingTrain : Train<string, bool>
    {
        protected override async Task<Either<Exception, bool>> Junctions()
        {
            await Task.Yield();
            return await Chain<StringLength>().Chain<IntToBool>().Resolve();
        }
    }

    private class DirectReturnTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Task.FromResult<Either<Exception, bool>>(true);
    }

    private class ValueResolvingTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<StringLength>().Resolve(true);
    }

    private class ThrowingAsyncTrain : Train<string, bool>
    {
        protected override async Task<Either<Exception, bool>> Junctions()
        {
            // An already-completed await keeps the body synchronous, so the throw lands in a
            // finished task rather than propagating out of the call.
            await Task.CompletedTask;
            throw new InvalidOperationException("declaration failed");
        }
    }

    private class ClassIChainTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            IChain<StringLength>().Chain<IntToBool>().Resolve();
    }

    private class ClassServiceTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            AddServices(new Counter()).Chain<StringLength>().Chain<IntToBool>().Resolve();
    }

    private class FreshMonadTrain : Train<string, int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            NewMonad().Chain<CountingJunction>().Resolve();
    }

    private class NotAJunction;

    private class NotAJunctionTrain : Train<string, int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<NotAJunction>().Chain<StringLength>().Resolve();
    }

    private class StringToUnit : Junction<string, LanguageExt.Unit>
    {
        public override Task<LanguageExt.Unit> Run(string input) =>
            Task.FromResult(LanguageExt.Unit.Default);
    }

    private class InstanceTrain : Train<string, int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            ShortCircuit(new CountingJunction()).Chain(new CountingJunction()).Resolve();
    }

    private class UnitInstanceFirstTrain : Train<string, LanguageExt.Unit>
    {
        protected override Task<Either<Exception, LanguageExt.Unit>> Junctions() =>
            Chain<StringToUnit, string>(new StringToUnit()).Chain<StringToUnit, string>().Resolve();
    }

    private class UnitParameterlessFirstTrain : Train<string, LanguageExt.Unit>
    {
        protected override Task<Either<Exception, LanguageExt.Unit>> Junctions() =>
            Chain<StringToUnit, string>().Chain<StringToUnit, string>(new StringToUnit()).Resolve();
    }

    private class GatedFailingTrain(Task gate) : Train<string, bool>
    {
        public const string Failure = "failed after the declaration was read";

        protected override async Task<Either<Exception, bool>> Junctions()
        {
            await gate;
            throw new InvalidOperationException(Failure);
        }
    }
}
