using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// Replaying a declared chain over the types Memory would hold.
///
/// <para>Memory is keyed by type, so whether a chain lines up is decidable from the declaration
/// alone. These pin the cases a host needs to be right about before it can refuse to start on
/// the strength of them.</para>
///
/// <para>Enforces Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0016-a-junction-chain-is-a-declaration-not-a-step-of-the-work.md")]
public class ChainVerificationTests : TestSetup
{
    private static IReadOnlyList<ChainFault> Verify<TTrain, TIn, TOut>()
        where TTrain : Train<TIn, TOut>, new() =>
        ChainVerification.Verify(new TTrain().DeclaredChain(), typeof(TIn), typeof(TOut));

    [Test]
    public void Verify_AChainThatLinesUp_ReportsNothing() =>
        Verify<WellFormedTrain, string, bool>().Should().BeEmpty();

    [Test]
    public void Verify_AJunctionWhoseInputNeverReachesMemory_IsReported()
    {
        var faults = Verify<MissingInputTrain, string, bool>();

        faults.Should().HaveCount(1);
        faults[0].Junction.Should().Be(typeof(IntToBool));
        faults[0].StepIndex.Should().Be(0, "the very first junction is the one that cannot run");
        faults[0]
            .Reason.Should()
            .Contain("Int32")
            .And.Contain("Chain a junction that produces it first");
    }

    [Test]
    public void Verify_AChainEndingWithoutItsReturnType_IsReported()
    {
        var faults = Verify<NeverProducesReturnTrain, string, bool>();

        faults.Should().ContainSingle(f => f.Kind == ChainStepKind.Resolve);
        faults
            .Single(f => f.Kind == ChainStepKind.Resolve)
            .Reason.Should()
            .Contain("nothing for Resolve to return");
    }

    [Test]
    public void Verify_AShortCircuitAlone_DoesNotSupplyTheReturnValue() =>
        Verify<ShortCircuitOnlyTrain, string, bool>()
            .Should()
            .ContainSingle(f => f.Kind == ChainStepKind.Resolve)
            .Which.Reason.Should()
            .Contain(
                "nothing for Resolve to return",
                "a short circuit that returns Left lets the chain run on, and nothing after it "
                    + "produces the return type"
            );

    [Test]
    public void Verify_AShortCircuitFollowedByAProducer_ReportsNothing() =>
        Verify<ShortCircuitThenProducerTrain, string, bool>().Should().BeEmpty();

    [Test]
    public void Verify_AShortCircuitsOutput_IsNotAvailableToLaterJunctions() =>
        Verify<ReadsShortCircuitOutputTrain, string, bool>()
            .Should()
            .ContainSingle(f => f.Junction == typeof(IntToBool))
            .Which.Reason.Should()
            .Contain(
                "Int32",
                "on the path that continues, the short circuit returned Left and stored nothing"
            );

    [Test]
    public async Task Verify_AnInterfaceAJunctionsOutputImplements_IsNotAvailable()
    {
        Verify<OutputInterfaceTrain, string, bool>()
            .Should()
            .ContainSingle(f => f.Junction == typeof(IngredientToBool))
            .Which.Reason.Should()
            .Contain("IIngredient");

        // The replay exists to predict the run, so the run has to fail the same way.
        var run = await new OutputInterfaceTrain().RunEither("flour");

        run.IsLeft.Should().BeTrue();
        run.Swap()
            .ValueUnsafe()
            .Message.Should()
            .Contain(
                "IIngredient",
                "a junction's output enters Memory under its declared type only"
            );
    }

    [Test]
    public void Verify_AValuePassedToAddServices_IsAvailableUnderThePassedType() =>
        Verify<AddServicesTrain, string, bool>()
            .Should()
            .BeEmpty("AddServices puts the value in Memory without a junction producing it");

    [Test]
    public void Verify_AValuePassedToExtract_IsAvailable() =>
        Verify<ExtractValueTrain, string, bool>().Should().BeEmpty();

    [Test]
    public async Task Verify_AnExtractFromAnEarlierJunctionsOutput_IsSatisfied()
    {
        Verify<ExtractFromProducedTrain, string, bool>()
            .Should()
            .BeEmpty("the junction before it put the Extract's source in Memory");

        var run = await new ExtractFromProducedTrain().RunEither("flour");
        run.IsRight.Should().BeTrue("the run finds the source where the replay did");
    }

    [Test]
    public void Verify_ExtractFromATypeOnlyTheContainerHolds_IsReported() =>
        ChainVerification
            .Verify(
                new ExtractFromAmbientTrain().DeclaredChain(),
                typeof(string),
                typeof(bool),
                type => type == typeof(IAmbient)
            )
            .Should()
            .ContainSingle(f => f.Kind == ChainStepKind.Extract)
            .Which.Reason.Should()
            .Contain("does not fall back to the container");

    [Test]
    public void Verify_IChainOfAJunctionNothingHolds_IsReported()
    {
        var chain = new InterfaceJunctionTrain().DeclaredChain();

        ChainVerification
            .Verify(chain, typeof(string), typeof(bool))
            .Should()
            .Contain(f => f.Kind == ChainStepKind.IChain && f.Reason.Contains("neither holds one"));

        ChainVerification
            .Verify(chain, typeof(string), typeof(bool), type => type == typeof(ILengthJunction))
            .Should()
            .BeEmpty("a registered junction interface is what IChain resolves");
    }

    [Test]
    public void Verify_ARefusedDeclaration_IsReportedAsAFault() =>
        Verify<ValueResolvingTrain, string, bool>()
            .Should()
            .ContainSingle(f => f.Reason.Contains("Resolve(value)"));

    [Test]
    public void Verify_ATupleOutput_MakesItsElementsAvailable() =>
        Verify<TupleProducingTrain, string, bool>()
            .Should()
            .BeEmpty("a tuple contributes each of its elements to Memory, not itself");

    [Test]
    public void Verify_AnInterfaceTheInputImplements_CountsAsPresent() =>
        Verify<InterfaceConsumingTrain, Ingredient, bool>()
            .Should()
            .BeEmpty("a value in Memory is reachable by every interface it implements");

    [Test]
    public void Verify_AJunctionTakingATuple_IsSatisfiedByItsElements() =>
        ChainVerification
            .Verify(new TupleConsumingTrain().DeclaredChain(), typeof(string), typeof(bool))
            .Should()
            .BeEmpty(
                "a tuple input is assembled from its elements rather than looked up whole, so "
                    + "each element being present is what satisfies it"
            );

    [Test]
    public void Verify_AJunctionTakingAnInjectedService_IsSatisfiedByTheContainer()
    {
        var chain = new ServiceConsumingTrain().DeclaredChain();

        ChainVerification
            .Verify(chain, typeof(string), typeof(bool))
            .Should()
            .ContainSingle("without the container, an injected input looks like a missing one");

        ChainVerification
            .Verify(chain, typeof(string), typeof(bool), type => type == typeof(IAmbient))
            .Should()
            .BeEmpty("a junction input not in Memory falls back to the container");
    }

    [Test]
    public async Task Verify_ATupleElementOnlyTheContainerHolds_IsReported()
    {
        // The runtime assembles a tuple from Memory alone, so a container-only element fails
        // every run however the container is configured.
        ChainVerification
            .Verify(
                new AmbientTupleTrain().DeclaredChain(),
                typeof(string),
                typeof(bool),
                type => type == typeof(IAmbient)
            )
            .Should()
            .ContainSingle(f => f.Junction == typeof(PairWithAmbientToFlag));

        var run = await new AmbientTupleTrain().RunEither("x");
        run.IsLeft.Should().BeTrue("the run cannot find the element either");
    }

    [Test]
    public async Task Verify_AJunctionTakingTheDeclaredInput_IsSatisfiedWhateverSubtypeIsPassed()
    {
        Verify<BaseInputTrain, Ingredient, bool>().Should().BeEmpty();

        // The run stores the input under its declared type as well as its concrete one.
        var run = await new BaseInputTrain().RunEither(new SpecialIngredient());
        run.IsRight.Should().BeTrue("a subtype of the declared input must satisfy it at runtime");
    }

    [Test]
    public void Verify_AShortCircuitThatCannotBeTheResult_IsReported() =>
        Verify<IncompatibleShortCircuitTrain, string, bool>()
            .Should()
            .Contain(f =>
                f.Kind == ChainStepKind.ShortCircuit
                && f.Reason.Contains("cannot be the train's result")
            );

    [Test]
    public async Task Verify_ATupleElementDeclaredAsABaseClass_IsSatisfiedWhateverSubtypeItHolds()
    {
        Verify<TupleBaseElementTrain, string, bool>().Should().BeEmpty();

        // The run stores each element under its declared type too, so the subtype inside the
        // tuple still satisfies a junction taking the base class.
        var run = await new TupleBaseElementTrain().RunEither("x");
        run.IsRight.Should().BeTrue();
    }

    [Test]
    public async Task A_null_tuple_element_fails_as_a_missing_type_rather_than_crashing()
    {
        var run = await new NullTupleElementTrain().RunEither("x");

        run.IsLeft.Should().BeTrue();
        run.Swap().ValueUnsafe().Should().NotBeOfType<NullReferenceException>();
    }

    private interface IIngredient;

    private record Ingredient : IIngredient;

    private class StringLength : Junction<string, int>
    {
        public override Task<int> Run(string input) => Task.FromResult(input.Length);
    }

    private class IntToBool : Junction<int, bool>
    {
        public override Task<bool> Run(int input) => Task.FromResult(input > 0);
    }

    private class StringToUnit : Junction<string, LanguageExt.Unit>
    {
        public override Task<LanguageExt.Unit> Run(string input) =>
            Task.FromResult(LanguageExt.Unit.Default);
    }

    private class StringToPair : Junction<string, (int Length, bool Empty)>
    {
        public override Task<(int, bool)> Run(string input) =>
            Task.FromResult((input.Length, input.Length == 0));
    }

    private class IngredientToBool : Junction<IIngredient, bool>
    {
        public override Task<bool> Run(IIngredient input) => Task.FromResult(true);
    }

    private interface IAmbient;

    private class PairToFlag : Junction<(int Length, bool Empty), bool>
    {
        public override Task<bool> Run((int Length, bool Empty) input) =>
            Task.FromResult(!input.Empty);
    }

    private class AmbientToFlag : Junction<IAmbient, bool>
    {
        public override Task<bool> Run(IAmbient input) => Task.FromResult(true);
    }

    private class TupleConsumingTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<StringToPair>().Chain<PairToFlag>().Resolve();
    }

    private class ServiceConsumingTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<AmbientToFlag>().Resolve();
    }

    private class WellFormedTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<StringLength>().Chain<IntToBool>().Resolve();
    }

    private class MissingInputTrain : Train<string, bool>
    {
        // IntToBool needs an int and nothing produces one.
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<IntToBool>().Resolve();
    }

    private class NeverProducesReturnTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<StringToUnit>().Resolve();
    }

    private class StringToFlag : Junction<string, bool>
    {
        public override Task<bool> Run(string input) => Task.FromResult(input.Length > 0);
    }

    private class StringToIngredient : Junction<string, Ingredient>
    {
        public override Task<Ingredient> Run(string input) => Task.FromResult(new Ingredient());
    }

    private interface ILengthJunction : IJunction<string, int>;

    private class Ambient : IAmbient
    {
        public int Level { get; } = 1;
    }

    private record Holder(int Count);

    private class ShortCircuitOnlyTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            ShortCircuit<StringToFlag>().Resolve();
    }

    private class ShortCircuitThenProducerTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            ShortCircuit<StringToFlag>().Chain<StringToFlag>().Resolve();
    }

    private class ReadsShortCircuitOutputTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            ShortCircuit<StringLength>().Chain<IntToBool>().Resolve();
    }

    private class OutputInterfaceTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<StringToIngredient>().Chain<IngredientToBool>().Resolve();
    }

    private class AddServicesTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            AddServices<IAmbient>(new Ambient()).Chain<AmbientToFlag>().Resolve();
    }

    private class ExtractValueTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Extract<Holder, int>(new Holder(2)).Chain<IntToBool>().Resolve();
    }

    private class ExtractFromAmbientTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Extract<IAmbient, int>().Chain<IntToBool>().Resolve();
    }

    private class InterfaceJunctionTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            IChain<ILengthJunction>().Chain<IntToBool>().Resolve();
    }

    private class ValueResolvingTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<StringToFlag>().Resolve(true);
    }

    private class TupleProducingTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<StringToPair>().Resolve();
    }

    private class InterfaceConsumingTrain : Train<Ingredient, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<IngredientToBool>().Resolve();
    }

    private record SpecialIngredient : Ingredient;

    private class IngredientToFlag : Junction<Ingredient, bool>
    {
        public override Task<bool> Run(Ingredient input) => Task.FromResult(true);
    }

    private class BaseInputTrain : Train<Ingredient, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<IngredientToFlag>().Resolve();
    }

    private class PairWithAmbientToFlag : Junction<(int Length, IAmbient Ambient), bool>
    {
        public override Task<bool> Run((int Length, IAmbient Ambient) input) =>
            Task.FromResult(true);
    }

    private class AmbientTupleTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<StringLength>().Chain<PairWithAmbientToFlag>().Resolve();
    }

    private class IncompatibleShortCircuitTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            ShortCircuit<StringLength>().Chain<StringToFlag>().Resolve();
    }

    private class StringToIngredientPair : Junction<string, (Ingredient Item, int Count)>
    {
        public override Task<(Ingredient, int)> Run(string input) =>
            Task.FromResult<(Ingredient, int)>((new SpecialIngredient(), 1));
    }

    private class TupleBaseElementTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<StringToIngredientPair>().Chain<IngredientToFlag>().Resolve();
    }

    private class StringToNullPair : Junction<string, (Ingredient? Item, int Count)>
    {
        public override Task<(Ingredient?, int)> Run(string input) =>
            Task.FromResult<(Ingredient?, int)>((null, 1));
    }

    private class NullTupleElementTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<StringToNullPair>().Chain<IngredientToFlag>().Resolve();
    }

    private class StringToHolder : Junction<string, Holder>
    {
        public override Task<Holder> Run(string input) => Task.FromResult(new Holder(input.Length));
    }

    private class ExtractFromProducedTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Chain<StringToHolder>().Extract<Holder, int>().Chain<IntToBool>().Resolve();
    }
}
