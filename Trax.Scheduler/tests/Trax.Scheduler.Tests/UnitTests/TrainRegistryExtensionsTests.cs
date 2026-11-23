using AwesomeAssertions;
using Trax.Mediator.Services.TrainRegistry;
using Trax.Scheduler.Extensions;

namespace Trax.Scheduler.Tests.UnitTests;

[TestFixture]
public class TrainRegistryExtensionsTests
{
    [Test]
    public void ValidateTrainRegistration_TypeRegistered_DoesNotThrow()
    {
        var registry = new StubRegistry
        {
            InputTypeToTrain = new Dictionary<Type, Type> { [typeof(MyInput)] = typeof(MyTrain) },
        };

        var act = () => registry.ValidateTrainRegistration<MyInput>();

        act.Should().NotThrow();
    }

    [Test]
    public void ValidateTrainRegistration_TypeNotRegistered_ThrowsInvalidOperation()
    {
        var registry = new StubRegistry();

        var act = () => registry.ValidateTrainRegistration<MyInput>();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*IServiceTrain<MyInput, TOut>*ScanAssemblies*");
    }

    [Test]
    public void ValidateTrainRegistration_DifferentInputType_StillThrows()
    {
        var registry = new StubRegistry
        {
            InputTypeToTrain = new Dictionary<Type, Type> { [typeof(MyInput)] = typeof(MyTrain) },
        };

        var act = () => registry.ValidateTrainRegistration<OtherInput>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*OtherInput*");
    }

    private sealed class StubRegistry : ITrainRegistry
    {
        public IReadOnlyDictionary<Type, Type> InputTypeToTrain { get; set; } =
            new Dictionary<Type, Type>();
    }

    private sealed class MyInput;

    private sealed class OtherInput;

    private static class MyTrain;
}
