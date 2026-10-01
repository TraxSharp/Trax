using Trax.Mediator.Testing;

namespace Trax.Mediator.Testing.Tests;

// Test-local stand-ins matching the Trax base type names ("ServiceTrain`2" / "IServiceTrain`2"),
// which the guard matches by name. Used to exercise the reflection guard without the real framework.
public interface IServiceTrain<TIn, TOut>;

public abstract class ServiceTrain<TIn, TOut>;

public interface IGoodTrain : IServiceTrain<int, int>;

public sealed class GoodTrain : ServiceTrain<int, int>, IGoodTrain;

/// <summary>A train interface not named after its class, which the registry accepts.</summary>
public interface IRenamedContract : IServiceTrain<string, int>;

public sealed class DifferentlyNamedTrain : ServiceTrain<string, int>, IRenamedContract;

/// <summary>A train that implements its interface directly, with no ServiceTrain base.</summary>
public interface IDirectTrain : IServiceTrain<long, int>;

public sealed class DirectTrain : IDirectTrain;

[TestFixture]
public class TrainGuardsTests
{
    [Test]
    public void EveryTrainHasInterface_FlagsTrainsMissingInterface()
    {
        var result = TrainGuards.EveryTrainHasInterface([
            typeof(GoodTrain).Assembly,
            BadTrainAssembly.Instance,
        ]);

        result
            .Inspected.Should()
            .BeGreaterThanOrEqualTo(2, "GoodTrain and BadTrain are both trains");
        result.Offenders.Should().Contain(o => o.Contains("BadTrain"));
        result.Offenders.Should().NotContain(o => o.Contains(nameof(GoodTrain)));
    }

    [Test]
    public void EveryTrainHasInterface_CountsATrainThatImplementsItsInterfaceDirectly()
    {
        var result = TrainGuards.EveryTrainHasInterface([typeof(DirectTrain).Assembly]);

        result
            .Inspected.Should()
            .Be(
                3,
                "GoodTrain, DifferentlyNamedTrain and DirectTrain are all trains the registry scans"
            );
        result.Offenders.Should().BeEmpty();
    }

    [Test]
    public void EveryTrainHasInterface_AcceptsAnyTrainInterfaceTheRegistryAccepts()
    {
        var result = TrainGuards.EveryTrainHasInterface([typeof(DifferentlyNamedTrain).Assembly]);

        result
            .Offenders.Should()
            .NotContain(
                o => o.Contains(nameof(DifferentlyNamedTrain)),
                "the registry registers it under IRenamedContract, its own train interface"
            );
    }

    [Test]
    public void EveryTrainHasInterface_FlagsATrainWithOnlyTheSharedInterface()
    {
        var result = TrainGuards.EveryTrainHasInterface([BadTrainAssembly.Instance]);

        result
            .Offenders.Should()
            .Contain(
                o => o.Contains("DirectOnlyTrain"),
                "with nothing more specific than IServiceTrain<,> it has no name of its own"
            );
    }

    [Test]
    public void EveryTrainHasInterface_FlagsATrainWithTwoUnrelatedTrainInterfaces()
    {
        var result = TrainGuards.EveryTrainHasInterface([BadTrainAssembly.Instance]);

        result
            .Offenders.Should()
            .Contain(o =>
                o.Contains("TwoFacedTrain") && o.Contains("IFirstFace") && o.Contains("ISecondFace")
            );
    }
}
