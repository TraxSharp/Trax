using Trax.Mediator.Testing;

namespace Trax.Mediator.Testing.Tests;

// Test-local stand-ins matching the Trax base type names ("ServiceTrain`2" / "IServiceTrain`2"),
// which the guard matches by name. Used to exercise the reflection guard without the real framework.
public interface IServiceTrain<TIn, TOut>;

public abstract class ServiceTrain<TIn, TOut>;

public interface IGoodTrain : IServiceTrain<int, int>;

public sealed class GoodTrain : ServiceTrain<int, int>, IGoodTrain;

public sealed class BadTrain : ServiceTrain<int, int>; // no companion interface

[TestFixture]
public class TrainGuardsTests
{
    [Test]
    public void EveryTrainHasInterface_FlagsTrainsMissingInterface()
    {
        var result = TrainGuards.EveryTrainHasInterface([typeof(GoodTrain).Assembly]);

        result
            .Inspected.Should()
            .BeGreaterThanOrEqualTo(2, "GoodTrain and BadTrain are both trains");
        result.Offenders.Should().Contain(o => o.Contains(nameof(BadTrain)));
        result.Offenders.Should().NotContain(o => o.Contains(nameof(GoodTrain)));
    }
}
