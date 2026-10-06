using System.Reflection;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.JunctionProvider.Logging.Services.JunctionLoggerProvider;
using Trax.Effect.Models.JunctionMetadata;
using Trax.Effect.Models.JunctionMetadata.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.LifecycleHookOutputPolicy;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Utils;

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
    public async Task AfterJunctionExecution_MasksASensitiveOutputProperty()
    {
        var (train, _) = BuildPair("AfterSensitive");
        var junction = new SensitiveEffectJunction();
        typeof(EffectJunction<string, Token>)
            .GetProperty("Metadata", BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(
                junction,
                JunctionMetadata.Create(
                    new CreateJunctionMetadata
                    {
                        Name = "AfterSensitive",
                        ExternalId = Guid.NewGuid().ToString("N"),
                        InputType = typeof(string),
                        OutputType = typeof(Token),
                        State = EitherStatus.IsRight,
                    },
                    train.Metadata!
                )
            );
        await junction.RailwayJunction(Either<Exception, string>.Right("ada"), train);

        var provider = new JunctionLoggerProvider(
            TestConfig(serializeJunctionData: true),
            NullLogger<JunctionLoggerProvider>.Instance
        );
        await provider.AfterJunctionExecution(junction, train, CancellationToken.None);

        junction.Metadata!.OutputJson.Should().Contain("ada").And.NotContain("tok-secret");
    }

    public sealed class Token
    {
        public string User { get; set; } = "";

        [TraxSensitive]
        public string Value { get; set; } = "";
    }

    private class SensitiveEffectJunction : EffectJunction<string, Token>
    {
        public override Task<Token> Run(string input) =>
            Task.FromResult(new Token { User = input, Value = "tok-secret" });
    }

    [Test]
    public async Task AfterJunctionExecution_OutputHoldingAType_WritesAPlaceholderInsteadOfThrowing()
    {
        var (train, junction) = BuildTypedPair<HoldsAType>(new HoldsAType { T = typeof(string) });

        var provider = new JunctionLoggerProvider(
            TestConfig(serializeJunctionData: true),
            NullLogger<JunctionLoggerProvider>.Instance
        );
        var act = async () =>
            await provider.AfterJunctionExecution(junction, train, CancellationToken.None);

        await act.Should()
            .NotThrowAsync("the junction already succeeded; logging it must not fail the train");
        junction.Metadata!.OutputJson.Should().Contain("\"_unserializable\":true");
    }

    [Test]
    public async Task AfterJunctionExecution_OutputDeeperThanTheSerializerAllows_WritesAPlaceholderInsteadOfThrowing()
    {
        var deep = new Nested();
        var cursor = deep;
        for (var i = 0; i < 10; i++)
            cursor = cursor.Next = new Nested();
        var (train, junction) = BuildTypedPair(deep);

        var provider = new JunctionLoggerProvider(
            TestConfig(serializeJunctionData: true),
            NullLogger<JunctionLoggerProvider>.Instance
        );
        var act = async () =>
            await provider.AfterJunctionExecution(junction, train, CancellationToken.None);

        await act.Should().NotThrowAsync();
        junction.Metadata!.OutputJson.Should().Contain("\"_unserializable\":true");
    }

    [Test]
    public async Task AfterJunctionExecution_OutputOverTheCeiling_IsTruncated()
    {
        var (train, junction) = BuildTypedPair(new string('x', 4096));
        var policy = new FixedOutputPolicy(1024);

        var provider = new JunctionLoggerProvider(
            TestConfig(serializeJunctionData: true),
            NullLogger<JunctionLoggerProvider>.Instance,
            policy
        );
        await provider.AfterJunctionExecution(junction, train, CancellationToken.None);

        junction.Metadata!.OutputJson.Should().Be(TraxBoundedJson.TruncatedPlaceholder(1024));
        policy.AskedFor.Should().Equal(train.TrainName);
    }

    [Test]
    public async Task AfterJunctionExecution_ForATrainWhoseOutputIsExcluded_SerializesNothing()
    {
        var (train, junction) = BuildTypedPair("keep me out of the log");

        var provider = new JunctionLoggerProvider(
            TestConfig(serializeJunctionData: true),
            NullLogger<JunctionLoggerProvider>.Instance,
            new FixedOutputPolicy(null)
        );
        await provider.AfterJunctionExecution(junction, train, CancellationToken.None);

        junction.Metadata!.OutputJson.Should().BeNull();
    }

    [Test]
    public async Task BeforeJunctionExecution_NullMetadata_SaysTheMetadataIsMissing()
    {
        var provider = new JunctionLoggerProvider(
            TestConfig(),
            NullLogger<JunctionLoggerProvider>.Instance
        );

        var act = async () =>
            await provider.BeforeJunctionExecution(
                new TestEffectJunction(),
                new TestTrain(),
                CancellationToken.None
            );

        (await act.Should().ThrowAsync<TrainException>())
            .Which.Message.Should()
            .Contain("should not be null");
    }

    public sealed class HoldsAType
    {
        public Type T { get; set; } = typeof(object);
    }

    public sealed class Nested
    {
        public Nested? Next { get; set; }
    }

    private sealed class FixedOutputPolicy(int? ceiling) : ILifecycleHookOutputPolicy
    {
        public List<string> AskedFor { get; } = [];

        public int? MaxCopyBytes(string trainName)
        {
            AskedFor.Add(trainName);
            return ceiling;
        }
    }

    private sealed class ReturnsJunction<T>(T value) : EffectJunction<string, T>
    {
        public override Task<T> Run(string input) => Task.FromResult(value);
    }

    private static (TestTrain train, ReturnsJunction<T> junction) BuildTypedPair<T>(T value)
    {
        var (train, _) = BuildPair(typeof(T).Name);
        var junction = new ReturnsJunction<T>(value);
        typeof(EffectJunction<string, T>)
            .GetProperty("Metadata", BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(
                junction,
                JunctionMetadata.Create(
                    new CreateJunctionMetadata
                    {
                        Name = typeof(T).Name,
                        ExternalId = Guid.NewGuid().ToString("N"),
                        InputType = typeof(string),
                        OutputType = typeof(T),
                        State = EitherStatus.IsRight,
                    },
                    train.Metadata!
                )
            );
        junction
            .RailwayJunction(Either<Exception, string>.Right("in"), train)
            .GetAwaiter()
            .GetResult();
        return (train, junction);
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
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<PassThrough1_263>().Resolve();

        private sealed class PassThrough1_263 : Junction<string, string>
        {
            public override Task<string> Run(string input) => Task.FromResult(input);
        }
    }

    private class TestEffectJunction : EffectJunction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult(input + "-out");
    }
}
