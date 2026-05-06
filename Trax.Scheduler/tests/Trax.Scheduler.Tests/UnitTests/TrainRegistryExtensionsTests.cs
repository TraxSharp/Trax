using FluentAssertions;
using Trax.Mediator.Services.TrainRegistry;
using Trax.Scheduler.Extensions;

namespace Trax.Scheduler.Tests.UnitTests;

[TestFixture]
public class TrainRegistryExtensionsTests
{
    [Test]
    public void ValidateTrainRegistration_TypeRegistered_DoesNotThrow()
    {
        var registry = new StubRegistry();
        registry.InputTypeToTrain[typeof(MyInput)] = typeof(MyTrain);

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
            .WithMessage("*MyInput*not registered*AddEffectTrainBus*");
    }

    [Test]
    public void ValidateTrainRegistration_DifferentInputType_StillThrows()
    {
        var registry = new StubRegistry();
        registry.InputTypeToTrain[typeof(MyInput)] = typeof(MyTrain);

        var act = () => registry.ValidateTrainRegistration<OtherInput>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*OtherInput*");
    }

    private sealed class StubRegistry : ITrainRegistry
    {
        public Dictionary<Type, Type> InputTypeToTrain { get; set; } = new();
    }

    private sealed class MyInput;

    private sealed class OtherInput;

    private sealed class MyTrain;
}
