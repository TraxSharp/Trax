using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Provider.Parameter.Configuration;
using Trax.Effect.Provider.Parameter.Services.ParameterEffectProviderFactory;

namespace Trax.Effect.Tests.Integration.UnitTests.Services;

[TestFixture]
public class ParameterEffectProviderFactoryTests
{
    [Test]
    public void Create_ReturnsParameterEffect()
    {
        var config = new StubConfig();
        var effectConfig = new ParameterEffectConfiguration
        {
            SaveInputs = true,
            SaveOutputs = true,
        };
        var factory = new ParameterEffectProviderFactory(config, effectConfig);

        var provider = factory.Create();

        provider.Should().BeOfType<ParameterEffect>();
    }

    [Test]
    public void Configuration_ReturnsConstructorArg()
    {
        var config = new StubConfig();
        var effectConfig = new ParameterEffectConfiguration();
        var factory = new ParameterEffectProviderFactory(config, effectConfig);

        factory.Configuration.Should().BeSameAs(effectConfig);
    }

    [Test]
    public void Create_MultipleCalls_ReturnsDistinctProviders()
    {
        var config = new StubConfig();
        var factory = new ParameterEffectProviderFactory(
            config,
            new ParameterEffectConfiguration()
        );

        var providers = new[] { factory.Create(), factory.Create(), factory.Create() };

        providers.Should().OnlyHaveUniqueItems();
    }

    private class StubConfig : ITraxEffectConfiguration
    {
        public JsonSerializerOptions SystemJsonSerializerOptions { get; } = new();
        public bool SerializeJunctionData => false;
        public LogLevel LogLevel => LogLevel.Information;
    }
}
