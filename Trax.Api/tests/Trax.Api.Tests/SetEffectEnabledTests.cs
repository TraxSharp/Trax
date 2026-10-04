using AwesomeAssertions;
using Trax.Api.GraphQL.Mutations;
using Trax.Effect.Services.EffectRegistry;

namespace Trax.Api.Tests;

/// <summary>
/// <c>operations.setEffectEnabled</c> toggles an effect through the same registry calls the
/// dashboard's effects page makes, so both refuse and apply the same things.
/// </summary>
[TestFixture]
public class SetEffectEnabledTests
{
    private sealed class ToggleableFactory;

    private sealed class FixedFactory;

    private EffectRegistry _registry = null!;

    [SetUp]
    public void SetUp()
    {
        _registry = new EffectRegistry();
        _registry.Register(typeof(ToggleableFactory), enabled: true, toggleable: true);
        _registry.Register(typeof(FixedFactory), enabled: true, toggleable: false);
    }

    [Test]
    public void Disabling_AToggleableEffect_TurnsItOff()
    {
        var result = new OperationsMutations().SetEffectEnabled(
            typeof(ToggleableFactory).FullName!,
            false,
            _registry
        );

        result.Success.Should().BeTrue();
        result.Count.Should().Be(1);
        _registry.IsEnabled(typeof(ToggleableFactory)).Should().BeFalse();
    }

    [Test]
    public void Enabling_ADisabledEffect_TurnsItBackOn()
    {
        _registry.Disable(typeof(ToggleableFactory));

        new OperationsMutations()
            .SetEffectEnabled(typeof(ToggleableFactory).FullName!, true, _registry)
            .Success.Should()
            .BeTrue();

        _registry.IsEnabled(typeof(ToggleableFactory)).Should().BeTrue();
    }

    [Test]
    public void ANotToggleableEffect_IsRefused_AndStaysAsItWas()
    {
        var result = new OperationsMutations().SetEffectEnabled(
            typeof(FixedFactory).FullName!,
            false,
            _registry
        );

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not toggleable");
        _registry.IsEnabled(typeof(FixedFactory)).Should().BeTrue();
    }

    [Test]
    public void AnUnknownEffect_IsRefused()
    {
        var result = new OperationsMutations().SetEffectEnabled(
            "No.Such.Factory",
            false,
            _registry
        );

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("No effect named");
    }

    [Test]
    public void TheNameIsMatchedExactly()
    {
        new OperationsMutations()
            .SetEffectEnabled(
                typeof(ToggleableFactory).FullName!.ToUpperInvariant(),
                false,
                _registry
            )
            .Success.Should()
            .BeFalse();
        _registry.IsEnabled(typeof(ToggleableFactory)).Should().BeTrue();
    }

    [Test]
    public void AnEffectTypeWithNoFullName_IsMatchedByItsName()
    {
        // A generic parameter is a Type with no FullName; the registry still keys on it.
        var unnamed = typeof(List<>).GetGenericArguments()[0];
        _registry.Register(unnamed, enabled: true, toggleable: true);

        new OperationsMutations().SetEffectEnabled("T", false, _registry).Success.Should().BeTrue();
        _registry.IsEnabled(unnamed).Should().BeFalse();
    }
}
