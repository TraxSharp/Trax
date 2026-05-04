using System.Text.Json;
using FluentAssertions;
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
    public void Create_ReturnsParameterEffectAndTracksProvider()
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
        factory.Providers.Should().ContainSingle().Which.Should().BeSameAs(provider);
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
    public void Create_MultipleCalls_AccumulatesProviders()
    {
        var config = new StubConfig();
        var factory = new ParameterEffectProviderFactory(
            config,
            new ParameterEffectConfiguration()
        );

        factory.Create();
        factory.Create();
        factory.Create();

        factory.Providers.Should().HaveCount(3);
    }

    private class StubConfig : ITraxEffectConfiguration
    {
        public JsonSerializerOptions SystemJsonSerializerOptions { get; } = new();
        public bool SerializeJunctionData => false;
        public LogLevel LogLevel => LogLevel.Information;
    }
}
