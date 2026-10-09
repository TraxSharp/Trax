using System.Text.Json;
using AwesomeAssertions;
using HotChocolate;
using HotChocolate.Execution;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// A train mutation whose input holds lists and arrays hands the execution service JSON that
/// <see cref="TrainInputReader.Read"/> reads back as the input the caller sent. The host's train
/// parameter options preserve references, which writes a list as an object of <c>$values</c>;
/// the reader refuses that, so a mutation written with those options failed before its train ran.
/// </summary>
[TestFixture]
public class DispatchListInputTests
{
    private const int MaxBytes = 262_144;

    private const string Input = """
        { pollId: 11, choiceKeys: ["11:32", "11:33"], ids: [-1, 4], stops: [{ city: "Lyon", zip: 69001 }], home: { city: "Nice", zip: 6000 } }
        """;

    private ITrainExecutionService _execution = null!;
    private ServiceProvider? _services;
    private string? _sent;

    [SetUp]
    public void SetUp()
    {
        _sent = null;
        _services = null;
        _execution = Substitute.For<ITrainExecutionService>();
        _execution
            .RunAsync(
                Arg.Any<string>(),
                Arg.Do<string>(json => _sent = json),
                Arg.Any<CancellationToken>()
            )
            .Returns(new RunTrainResult(1, "ext-1"));
        _execution
            .QueueAsync(
                Arg.Any<string>(),
                Arg.Do<string?>(json => _sent = json),
                Arg.Any<int>(),
                Arg.Any<DateTime?>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new QueueTrainResult(2, "ext-2"));
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_services is not null)
            await _services.DisposeAsync();
    }

    [Test]
    public void HostOptions_PreserveReferences()
    {
        // The host's options really do write a list as $values, or the tests below prove nothing.
        JsonSerializer
            .Serialize(
                new VoteInput { ChoiceKeys = ["a"] },
                TraxEffectConfiguration.StaticSystemJsonSerializerOptions
            )
            .Should()
            .Contain("\"$values\"");
    }

    [TestCase("RUN")]
    [TestCase("QUEUE")]
    public async Task ListInput_ReachesTheReader_AsTheInputTheCallerSent(string mode)
    {
        var executor = await BuildAsync();

        var body = (
            await executor.ExecuteAsync(
                $$"""mutation { dispatch { vote(input: {{Input}}, mode: {{mode}}) { __typename } } }"""
            )
        )
            .ExpectOperationResult()
            .ToJson();

        body.Should().NotContain("\"errors\"", body);
        _sent.Should().NotBeNull().And.NotContain("$id").And.NotContain("$values");

        var read = (VoteInput)TrainInputReader.Read(_sent, Registration(), MaxBytes);

        read.PollId.Should().Be(11);
        read.ChoiceKeys.Should().Equal("11:32", "11:33");
        read.Ids.Should().Equal(-1, 4);
        read.Stops.Should().ContainSingle().Which.Should().BeEquivalentTo(new Place("Lyon", 69001));
        read.Home.Should().BeEquivalentTo(new Place("Nice", 6000));
    }

    private async Task<IRequestExecutor> BuildAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TraxMarker>();
        var discovery = Substitute.For<ITrainDiscoveryService>();
        discovery.DiscoverTrains().Returns([Registration()]);
        services.AddSingleton(discovery);
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddTraxGraphQL(graphql =>
            graphql.ExposeOperationQueries().AllowAnonymousOperations()
        );
        services.AddScoped(_ => Substitute.For<ITraxHealthService>());
        services.AddScoped(_ => Substitute.For<IOperationsService>());
        services.AddScoped(_ => _execution);
        services.AddScoped(_ => Substitute.For<ITraxScheduler>());
        _services = services.BuildServiceProvider();
        return await _services
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
    }

    private static TrainRegistration Registration() =>
        new()
        {
            ServiceType = typeof(IVoteTrain),
            ImplementationType = typeof(VoteTrain),
            InputType = typeof(VoteInput),
            OutputType = typeof(Trax.Core.Functional.Unit),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = typeof(IVoteTrain).FullName!,
            ImplementationTypeName = nameof(VoteTrain),
            InputTypeName = nameof(VoteInput),
            OutputTypeName = nameof(Trax.Core.Functional.Unit),
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = false,
            IsMutation = true,
            IsBroadcastEnabled = false,
            HasAllowAnonymousAttribute = true,
            GraphQLName = "vote",
            GraphQLOperations = GraphQLOperation.Run | GraphQLOperation.Queue,
            IsRemote = false,
        };

    private interface IVoteTrain;

    private sealed class VoteTrain;

    public sealed record Place(string City, int Zip);

    public sealed record VoteInput
    {
        public long PollId { get; init; }
        public IReadOnlyList<string> ChoiceKeys { get; init; } = [];
        public int[] Ids { get; init; } = [];
        public List<Place> Stops { get; init; } = [];
        public Place? Home { get; init; }
    }
}
