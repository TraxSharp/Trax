using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Mutations;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Services.EffectRegistry;
using Trax.Scheduler.Services.Effects;

namespace Trax.Api.Tests;

/// <summary>
/// <c>operations.setEffectEnabled</c> and <c>operations.configureEffect</c> act through
/// <see cref="IEffectSettingsService"/>, the service the dashboard's effects page calls, so both
/// surfaces refuse and apply the same things.
/// </summary>
[TestFixture]
public class SetEffectEnabledTests
{
    private sealed class ToggleableFactory;

    private sealed class FixedFactory;

    public sealed class SinkSettings
    {
        [System.ComponentModel.DataAnnotations.Range(1, 1000)]
        public int BatchSize { get; set; } = 50;

        public string Target { get; set; } = "sink";

        [TraxSensitive]
        public string ApiKey { get; set; } = "sk-live-123";
    }

    public sealed class SinkFactory : IConfigurableProviderFactory
    {
        public SinkSettings Settings { get; } = new();

        public object GetConfiguration() => Settings;

        public Type GetConfigurationType() => typeof(SinkSettings);
    }

    private EffectRegistry _registry = null!;
    private SinkFactory _sink = null!;
    private ServiceProvider _services = null!;
    private IEffectSettingsService _settings = null!;

    [SetUp]
    public void SetUp()
    {
        _registry = new EffectRegistry();
        _registry.Register(typeof(ToggleableFactory), enabled: true, toggleable: true);
        _registry.Register(typeof(FixedFactory), enabled: true, toggleable: false);
        _registry.Register(typeof(SinkFactory), enabled: true, toggleable: true);
        _sink = new SinkFactory();
        _services = new ServiceCollection()
            .AddSingleton<IEffectRegistry>(_registry)
            .AddSingleton(_sink)
            .BuildServiceProvider();
        _settings = new EffectSettingsService(_services);
    }

    [TearDown]
    public void TearDown() => _services.Dispose();

    [Test]
    public void Disabling_AToggleableEffect_TurnsItOff()
    {
        var result = new OperationsMutations().SetEffectEnabled(
            typeof(ToggleableFactory).FullName!,
            false,
            _settings
        );

        result.Success.Should().BeTrue();
        result.Count.Should().Be(1);
        result.Message.Should().Be("Effect disabled in this process");
        _registry.IsEnabled(typeof(ToggleableFactory)).Should().BeFalse();
    }

    [Test]
    public void Enabling_ADisabledEffect_TurnsItBackOn()
    {
        _registry.Disable(typeof(ToggleableFactory));

        new OperationsMutations()
            .SetEffectEnabled(typeof(ToggleableFactory).FullName!, true, _settings)
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
            _settings
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
            _settings
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
                _settings
            )
            .Success.Should()
            .BeFalse();
        _registry.IsEnabled(typeof(ToggleableFactory)).Should().BeTrue();
    }

    [Test]
    public void AHostWithNoEffectRegistry_IsRefused()
    {
        using var empty = new ServiceCollection().BuildServiceProvider();

        new OperationsMutations()
            .SetEffectEnabled(
                typeof(ToggleableFactory).FullName!,
                false,
                new EffectSettingsService(empty)
            )
            .Success.Should()
            .BeFalse();
    }

    [Test]
    public void ConfigureEffect_WritesTheGivenSettings_AndLeavesTheRest()
    {
        var result = Configure(new("BatchSize", "200"), new("ApiKey", "sk-rotated"));

        result.Success.Should().BeTrue(result.Message);
        result.Count.Should().Be(2);
        result.Errors.Should().BeEmpty();
        _sink.Settings.BatchSize.Should().Be(200);
        _sink.Settings.ApiKey.Should().Be("sk-rotated", "a sensitive setting can be written");
        _sink.Settings.Target.Should().Be("sink");
    }

    [Test]
    public void ConfigureEffect_AnInvalidValue_WritesNothing_AndNamesTheSetting()
    {
        var result = Configure(new("Target", "elsewhere"), new("BatchSize", "5000"));

        result.Success.Should().BeFalse();
        result.Count.Should().Be(0);
        result.Errors.Should().ContainSingle().Which.Field.Should().Be("BatchSize");
        _sink.Settings.Target.Should().Be("sink", "the change is all or nothing");
        _sink.Settings.BatchSize.Should().Be(50);
    }

    [Test]
    public void ConfigureEffect_AnUnknownSetting_IsRefused()
    {
        var result = Configure(new EffectSettingValueInput("NoSuchSetting", "1"));

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Field.Should().Be("NoSuchSetting");
    }

    [Test]
    public void ConfigureEffect_ASettingGivenTwice_IsRefusedBeforeAnyIsWritten()
    {
        var result = Configure(new("BatchSize", "10"), new("BatchSize", "20"));

        result.Success.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Field.Should().Be("BatchSize");
        _sink.Settings.BatchSize.Should().Be(50);
    }

    [Test]
    public void ConfigureEffect_MoreThanTheBatchCap_IsRefused()
    {
        var values = Enumerable
            .Range(0, 1001)
            .Select(i => new EffectSettingValueInput($"S{i}", "1"))
            .ToList();

        var result = new OperationsMutations().ConfigureEffect(
            typeof(SinkFactory).FullName!,
            values,
            _settings
        );

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("1000");
    }

    [Test]
    public void ConfigureEffect_AnEffectWithNoSettings_IsRefused()
    {
        var result = new OperationsMutations().ConfigureEffect(
            typeof(ToggleableFactory).FullName!,
            [new EffectSettingValueInput("BatchSize", "1")],
            _settings
        );

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("no settings");
    }

    private ConfigureEffectResponse Configure(params EffectSettingValueInput[] values) =>
        new OperationsMutations().ConfigureEffect(typeof(SinkFactory).FullName!, values, _settings);
}
