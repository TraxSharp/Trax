using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Services.EffectRegistry;
using Trax.Scheduler.Services.Effects;
using RangeAttribute = System.ComponentModel.DataAnnotations.RangeAttribute;

namespace Trax.Scheduler.Tests.UnitTests;

/// <summary>
/// The effects list, toggle and settings editor the dashboard's effects page and the GraphQL API
/// share. The editor reads every value as its setting's type, checks it, and writes all or none
/// to the live settings object, and a <c>[TraxSensitive]</c> setting is never read back.
/// </summary>
[TestFixture]
public class EffectSettingsServiceTests
{
    public enum Mode
    {
        Slow,
        Fast,
    }

    public sealed class Settings
    {
        [Range(1, 100)]
        public int BatchSize { get; set; } = 10;

        public bool Verbose { get; set; }

        public string Endpoint { get; set; } = "https://example.test";

        public string? Note { get; set; } = "kept";

        [TraxSensitive]
        public string? ApiKey { get; set; } = "secret-key-value";

        public Mode Mode { get; set; } = Mode.Slow;

        public TimeSpan? Timeout { get; set; }

        public List<string>? Tags { get; set; } = ["a"];

        private int _exploding;

        public int Exploding
        {
            get => _exploding;
            set => _exploding = value == 13 ? throw new ArgumentException("13 is unlucky") : value;
        }
    }

    public sealed class ConfigurableFactory : IConfigurableProviderFactory
    {
        public Settings Settings { get; } = new();

        public object GetConfiguration() => Settings;

        public Type GetConfigurationType() => typeof(Settings);
    }

    public sealed class PlainFactory;

    public sealed class FixedFactory;

    private ConfigurableFactory _factory = null!;
    private EffectRegistry _registry = null!;
    private IEffectSettingsService _service = null!;

    private static string FullName<T>() => typeof(T).FullName!;

    [SetUp]
    public void SetUp()
    {
        _factory = new ConfigurableFactory();
        _registry = new EffectRegistry();
        _registry.Register(typeof(ConfigurableFactory));
        _registry.Register(typeof(PlainFactory), enabled: false);
        _registry.Register(typeof(FixedFactory), toggleable: false);

        var services = new ServiceCollection()
            .AddSingleton<IEffectRegistry>(_registry)
            .AddSingleton(_factory)
            .BuildServiceProvider();
        _service = new EffectSettingsService(services);
    }

    [Test]
    public void Effects_are_listed_by_full_name_with_their_state()
    {
        var effects = _service.GetEffects();

        effects.Select(e => e.FullName).Should().BeInAscendingOrder(StringComparer.Ordinal);
        var plain = effects.Single(e => e.FullName == FullName<PlainFactory>());
        plain.Enabled.Should().BeFalse();
        plain.IsConfigurable.Should().BeFalse();
        plain.Fields.Should().BeEmpty();
        effects.Single(e => e.FullName == FullName<FixedFactory>()).Toggleable.Should().BeFalse();
    }

    [Test]
    public void A_configurable_effect_describes_each_setting()
    {
        var effect = _service.GetEffects().Single(e => e.IsConfigurable);
        var fields = effect.Fields.ToDictionary(f => f.Name);

        effect.ConfigurationTypeName.Should().Be(typeof(Settings).FullName);
        fields["BatchSize"].Kind.Should().Be(EffectFieldKind.Text);
        fields["BatchSize"].Value.Should().Be("10");
        fields["BatchSize"].Nullable.Should().BeFalse();
        fields["Verbose"].Kind.Should().Be(EffectFieldKind.Boolean);
        fields["Verbose"].Value.Should().Be("false");
        fields["Mode"].Kind.Should().Be(EffectFieldKind.Enum);
        fields["Mode"].EnumValues.Should().Equal("Slow", "Fast");
        fields["Timeout"].Nullable.Should().BeTrue();
        fields["Timeout"].HasValue.Should().BeFalse();
        fields["Tags"].Kind.Should().Be(EffectFieldKind.SetInCode);
        fields["Tags"].HasValue.Should().BeTrue();
        fields["Tags"].Value.Should().BeNull();
        effect
            .Fields[^1]
            .Kind.Should()
            .Be(EffectFieldKind.SetInCode, "editable settings come first");
    }

    [Test]
    public void A_sensitive_setting_is_never_read_back()
    {
        var effect = _service.GetEffects().Single(e => e.IsConfigurable);
        var apiKey = effect.Fields.Single(f => f.Name == "ApiKey");

        apiKey.Sensitive.Should().BeTrue();
        apiKey.HasValue.Should().BeTrue();
        apiKey.Value.Should().BeNull();
        effect.Configuration.Should().NotContain("secret-key-value");
        effect.Configuration.Should().Contain("_redacted");
    }

    [Test]
    public void Toggling_refuses_an_unknown_or_fixed_effect_and_sets_a_toggleable_one()
    {
        _service.SetEffectEnabled("No.Such.Factory", false).Success.Should().BeFalse();
        _service.SetEffectEnabled(FullName<FixedFactory>(), false).Success.Should().BeFalse();
        _registry.IsEnabled(typeof(FixedFactory)).Should().BeTrue();

        var result = _service.SetEffectEnabled(FullName<PlainFactory>(), true);

        result.Success.Should().BeTrue(result.Message);
        result.Count.Should().Be(1);
        _registry.IsEnabled(typeof(PlainFactory)).Should().BeTrue();
    }

    [Test]
    public void Configuring_reads_each_value_as_its_type_and_writes_only_those_named()
    {
        var result = _service.ConfigureEffect(
            FullName<ConfigurableFactory>(),
            new Dictionary<string, string?>
            {
                ["BatchSize"] = "42",
                ["Verbose"] = "true",
                ["Mode"] = "Fast",
                ["Timeout"] = "00:00:05",
                ["Endpoint"] = " ",
                ["ApiKey"] = "new-secret",
            }
        );

        result.Success.Should().BeTrue(result.Message);
        result.Count.Should().Be(6);
        var settings = _factory.Settings;
        settings.BatchSize.Should().Be(42);
        settings.Verbose.Should().BeTrue();
        settings.Mode.Should().Be(Mode.Fast);
        settings.Timeout.Should().Be(TimeSpan.FromSeconds(5));
        settings.Endpoint.Should().BeEmpty("blank is the empty string for a non-nullable string");
        settings.ApiKey.Should().Be("new-secret", "a sensitive setting can be written");
        settings.Note.Should().Be("kept", "a setting not named is not written");
    }

    [Test]
    public void Blank_is_null_for_a_nullable_setting_and_refused_for_a_value_type()
    {
        _service
            .ConfigureEffect(
                FullName<ConfigurableFactory>(),
                new Dictionary<string, string?> { ["Note"] = null }
            )
            .Success.Should()
            .BeTrue();
        _factory.Settings.Note.Should().BeNull();

        var refused = _service.ConfigureEffect(
            FullName<ConfigurableFactory>(),
            new Dictionary<string, string?> { ["BatchSize"] = "" }
        );

        refused.Success.Should().BeFalse();
        refused.Errors.Should().ContainKey("BatchSize");
    }

    [TestCase("BatchSize", "500", TestName = "A_value_its_validation_refuses_writes_nothing")]
    [TestCase("BatchSize", "1,000", TestName = "A_value_that_does_not_parse_writes_nothing")]
    [TestCase("Mode", "Medium", TestName = "An_unknown_enum_member_writes_nothing")]
    [TestCase("Tags", "x", TestName = "A_setting_set_in_code_writes_nothing")]
    [TestCase("Nope", "1", TestName = "An_unknown_setting_writes_nothing")]
    public void A_refused_value_writes_none_of_the_others(string name, string value)
    {
        var result = _service.ConfigureEffect(
            FullName<ConfigurableFactory>(),
            new Dictionary<string, string?> { ["Verbose"] = "true", [name] = value }
        );

        result.Success.Should().BeFalse();
        result.Count.Should().Be(0);
        result.Errors.Keys.Should().Equal(name);
        _factory.Settings.Verbose.Should().BeFalse("every value is checked before any is written");
    }

    [Test]
    public void A_setter_that_throws_puts_back_the_settings_already_written()
    {
        var result = _service.ConfigureEffect(
            FullName<ConfigurableFactory>(),
            new Dictionary<string, string?> { ["BatchSize"] = "50", ["Exploding"] = "13" }
        );

        result.Success.Should().BeFalse();
        result.Errors["Exploding"].Should().Contain("unlucky");
        _factory.Settings.BatchSize.Should().Be(10, "the write is all or none");
    }

    [Test]
    public void Configuring_refuses_an_effect_without_settings_and_an_empty_list()
    {
        _service
            .ConfigureEffect(
                FullName<PlainFactory>(),
                new Dictionary<string, string?> { ["A"] = "1" }
            )
            .Success.Should()
            .BeFalse();
        _service
            .ConfigureEffect(FullName<ConfigurableFactory>(), new Dictionary<string, string?>())
            .Success.Should()
            .BeFalse();
    }

    [Test]
    public void Without_a_registry_nothing_is_listed_and_every_change_is_refused()
    {
        var service = new EffectSettingsService(new ServiceCollection().BuildServiceProvider());

        service.IsAvailable.Should().BeFalse();
        service.GetEffects().Should().BeEmpty();
        service.SetEffectEnabled(FullName<PlainFactory>(), true).Success.Should().BeFalse();
        service
            .ConfigureEffect(
                FullName<ConfigurableFactory>(),
                new Dictionary<string, string?> { ["Verbose"] = "true" }
            )
            .Success.Should()
            .BeFalse();
    }
}
