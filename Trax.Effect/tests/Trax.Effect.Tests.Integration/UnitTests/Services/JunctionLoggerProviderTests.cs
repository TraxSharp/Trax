using System.Reflection;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Trax.Core.Exceptions;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerProvider;
using Trax.Effect.Models.JunctionMetadata;
using Trax.Effect.Models.JunctionMetadata.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.UnitTests.Services;

[TestFixture]
public class JunctionLoggerProviderTests
{
    private static ITraxEffectConfiguration TestConfig(bool serializeJunctionData = true) =>
        new TraxEffectConfiguration
        {
            LogLevel = LogLevel.Information,
            SerializeJunctionData = serializeJunctionData,
        };

    private static (TestTrain train, TestEffectJunction junction) BuildPair(string name)
    {
        var train = new TestTrain();
        var trainMeta = Metadata.Create(
            new CreateMetadata
            {
                Name = "TestTrain",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        typeof(ServiceTrain<string, string>)
            .GetProperty("Metadata", BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(train, trainMeta);

        var junction = new TestEffectJunction();
        var junctionMeta = JunctionMetadata.Create(
            new CreateJunctionMetadata
            {
                Name = name,
                ExternalId = Guid.NewGuid().ToString("N"),
                InputType = typeof(string),
                OutputType = typeof(string),
                State = EitherStatus.IsRight,
            },
            trainMeta
        );
        typeof(EffectJunction<string, string>)
            .GetProperty("Metadata", BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(junction, junctionMeta);

        return (train, junction);
    }

    [Test]
    public async Task BeforeJunctionExecution_LogsAtConfiguredLevel()
    {
        var (train, junction) = BuildPair("BeforeTest");
        var provider = new JunctionLoggerProvider(
            TestConfig(),
            NullLogger<JunctionLoggerProvider>.Instance
        );

        await provider.BeforeJunctionExecution(junction, train, CancellationToken.None);
    }

    [Test]
    public async Task BeforeJunctionExecution_NullMetadata_Throws()
    {
        var train = new TestTrain();
        var junction = new TestEffectJunction(); // no metadata set
        var provider = new JunctionLoggerProvider(
            TestConfig(),
            NullLogger<JunctionLoggerProvider>.Instance
        );

        Func<Task> act = async () =>
            await provider.BeforeJunctionExecution(junction, train, CancellationToken.None);

        await act.Should().ThrowAsync<TrainException>().WithMessage("*Metadata*");
    }

    [Test]
    public async Task AfterJunctionExecution_NullMetadata_Throws()
    {
        var train = new TestTrain();
        var junction = new TestEffectJunction();
        var provider = new JunctionLoggerProvider(
            TestConfig(),
            NullLogger<JunctionLoggerProvider>.Instance
        );

        Func<Task> act = async () =>
            await provider.AfterJunctionExecution(junction, train, CancellationToken.None);

        await act.Should().ThrowAsync<TrainException>().WithMessage("*Metadata*");
    }

    [Test]
    public async Task AfterJunctionExecution_AfterRailwayRun_SerializesRightOutput()
    {
        var (train, junction) = BuildPair("AfterRight");

        // Drive a real RailwayJunction execution so Result is populated by the junction itself.
        await junction.RailwayJunction(Either<Exception, string>.Right("hello"), train);

        var provider = new JunctionLoggerProvider(
            TestConfig(serializeJunctionData: true),
            NullLogger<JunctionLoggerProvider>.Instance
        );

        await provider.AfterJunctionExecution(junction, train, CancellationToken.None);

        junction.Metadata!.OutputJson.Should().NotBeNull();
        junction.Metadata.OutputJson!.Should().Contain("hello-out");
    }

    [Test]
    public async Task AfterJunctionExecution_SerializeDisabled_LeavesOutputJsonNull()
    {
        var (train, junction) = BuildPair("AfterNoSerialize");

        await junction.RailwayJunction(Either<Exception, string>.Right("v"), train);

        var provider = new JunctionLoggerProvider(
            TestConfig(serializeJunctionData: false),
            NullLogger<JunctionLoggerProvider>.Instance
        );

        await provider.AfterJunctionExecution(junction, train, CancellationToken.None);

        junction.Metadata!.OutputJson.Should().BeNull();
    }

    [Test]
    public void Dispose_DoesNotThrow()
    {
        var provider = new JunctionLoggerProvider(
            TestConfig(),
            NullLogger<JunctionLoggerProvider>.Instance
        );

        Action act = () => provider.Dispose();
        act.Should().NotThrow();
    }

    private class TestTrain : ServiceTrain<string, string>
    {
        protected override Task<Either<Exception, string>> RunInternal(string input) =>
            Task.FromResult<Either<Exception, string>>(input);
    }

    private class TestEffectJunction : EffectJunction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult(input + "-out");
    }
}
