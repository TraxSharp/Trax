using FluentAssertions;
using HotChocolate.Execution;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.Services.HealthCheck;
using Trax.Api.Tests.Fakes;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Tests;

/// <summary>
/// <c>MaxOperationsPerRequest</c> counts the operations a request invokes, wherever they sit: at
/// the root, and under the namespaces (<c>dispatch</c>, <c>discover</c>, <c>operations</c>, their
/// nested namespaces and a train's declared <c>Namespace</c>). A namespace field is not an
/// operation itself; the selections under it are, after fragment expansion, counted once per
/// response path.
/// <para>Enforces <c>docs/adr/0029-the-operation-cap-counts-the-operations-under-each-namespace.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0029-the-operation-cap-counts-the-operations-under-each-namespace.md")]
[TestFixture]
public class OperationCapUnderNamespacesTests
{
    private const string Adr =
        "docs/adr/0029-the-operation-cap-counts-the-operations-under-each-namespace.md";

    private const string TooMany = "TRAX_TOO_MANY_OPERATIONS";

    private ServiceProvider? _provider;

    [TearDown]
    public async Task TearDown()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
    }

    [Test]
    public async Task FiftyOneAliasedTrainCallsUnderDispatch_AreRefusedAtTheDefaultCap()
    {
        var executor = await BuildExecutorAsync();

        var result = await executor.ExecuteAsync($"mutation {{ dispatch {{ {Calls(51)} }} }}");

        ErrorCodes(result)
            .Should()
            .Contain(TooMany, "the cap counts the operations under each namespace, per " + Adr);
    }

    [Test]
    public async Task FiftyAliasedTrainCallsUnderDispatch_AreWithinTheDefaultCap()
    {
        var executor = await BuildExecutorAsync();

        var result = await executor.ExecuteAsync($"mutation {{ dispatch {{ {Calls(50)} }} }}");

        ErrorCodes(result)
            .Should()
            .NotContain(TooMany, "the cap counts each operation once, per " + Adr);
    }

    [Test]
    public async Task TrainCallsUnderADeclaredNamespace_Count()
    {
        var executor = await BuildExecutorAsync();

        var result = await executor.ExecuteAsync(
            $"mutation {{ dispatch {{ billing {{ {Calls(51, "chargeCard")} }} }} }}"
        );

        ErrorCodes(result)
            .Should()
            .Contain(TooMany, "the cap counts the operations under each namespace, per " + Adr);
    }

    [Test]
    public async Task AnAliasedNamespaceField_CountsTheCallsUnderEachAlias()
    {
        var executor = await BuildExecutorAsync();

        var result = await executor.ExecuteAsync(
            $"mutation {{ x: dispatch {{ {Calls(26)} }} y: dispatch {{ {Calls(25)} }} }}"
        );

        ErrorCodes(result)
            .Should()
            .Contain(TooMany, "the cap counts the operations under each namespace, per " + Adr);
    }

    [Test]
    public async Task CallsReachedThroughAFragmentUnderDispatch_Count()
    {
        var executor = await BuildExecutorAsync();

        var result = await executor.ExecuteAsync(
            $"mutation {{ dispatch {{ ...Calls }} }} fragment Calls on DispatchMutations {{ {Calls(51)} }}"
        );

        ErrorCodes(result)
            .Should()
            .Contain(TooMany, "the cap counts the operations under each namespace, per " + Adr);
    }

    [Test]
    public async Task TheSameCallRepeatedUnderDispatch_CountsOnce()
    {
        var executor = await BuildExecutorAsync();
        var repeated = string.Join(" ", Enumerable.Repeat(Call("c0", "refund"), 60));

        var result = await executor.ExecuteAsync(
            $"mutation {{ dispatch {{ {repeated} }} dispatch {{ {repeated} }} }}"
        );

        ErrorCodes(result)
            .Should()
            .NotContain(TooMany, "the cap counts each operation once, per " + Adr);
    }

    [Test]
    public async Task AdminCallsUnderANestedOperationsNamespace_Count()
    {
        var executor = await BuildExecutorAsync();
        var requeues = string.Join(
            " ",
            Enumerable
                .Range(0, 51)
                .Select(i => $"r{i}: requeueDeadLetter(id: {i}) {{ __typename }}")
        );

        var result = await executor.ExecuteAsync(
            $"mutation {{ operations {{ deadLetters {{ {requeues} }} }} }}"
        );

        ErrorCodes(result)
            .Should()
            .Contain(TooMany, "the cap counts the operations under each namespace, per " + Adr);
    }

    [Test]
    public async Task QueryTrainCallsUnderDiscover_Count()
    {
        var executor = await BuildExecutorAsync();
        var lookups = string.Join(
            " ",
            Enumerable
                .Range(0, 51)
                .Select(i => $"l{i}: lookup(input: {{ value: \"{i}\" }}) {{ __typename }}")
        );

        var result = await executor.ExecuteAsync($"{{ discover {{ {lookups} }} }}");

        ErrorCodes(result)
            .Should()
            .Contain(TooMany, "the cap counts the operations under each namespace, per " + Adr);
    }

    [Test]
    public async Task ALowerCap_CountsCallsUnderNamespaces()
    {
        var executor = await BuildExecutorAsync(cap: 3);

        var atCap = await executor.ExecuteAsync($"mutation {{ dispatch {{ {Calls(3)} }} }}");
        var overCap = await executor.ExecuteAsync(
            $"mutation {{ dispatch {{ {Calls(2)} billing {{ {Calls(2, "chargeCard")} }} }} }}"
        );

        ErrorCodes(atCap)
            .Should()
            .NotContain(TooMany, "the cap counts each operation once, per " + Adr);
        ErrorCodes(overCap)
            .Should()
            .Contain(TooMany, "the cap counts the operations under each namespace, per " + Adr);
    }

    private static string Call(string alias, string field) =>
        $"{alias}: {field}(input: {{ value: \"x\" }}) {{ externalId }}";

    private static string Calls(int count, string field = "refund") =>
        string.Join(" ", Enumerable.Range(0, count).Select(i => Call($"c{i}", field)));

    private static IReadOnlyList<string?> ErrorCodes(IExecutionResult result) =>
        result.ExpectOperationResult().Errors?.Select(e => e.Code).ToList() ?? [];

    private async Task<IRequestExecutor> BuildExecutorAsync(int? cap = null)
    {
        var services = new ServiceCollection().AddDevelopmentEnvironment();
        services.AddSingleton<TraxMarker>();
        services.AddSingleton(Substitute.For<IEffectRegistry>());
        services.AddSingleton<ITrainDiscoveryService>(
            new StubDiscovery([
                Train<IRefundTrain>(isQuery: false, ns: null),
                Train<IChargeCardTrain>(isQuery: false, ns: "billing"),
                Train<ILookupTrain>(isQuery: true, ns: null),
            ])
        );
        services.AddScoped(_ => Substitute.For<ITraxScheduler>());
        services.AddScoped(_ => Substitute.For<ITraxHealthService>());

        services.AddTraxGraphQL(b =>
        {
            b = b.ExposeOperationQueries().ExposeOperationMutations().AllowAnonymousOperations();
            return cap is { } max ? b.MaxOperationsPerRequest(max) : b;
        });

        _provider = services.BuildServiceProvider();
        return await _provider
            .GetRequiredService<IRequestExecutorProvider>()
            .GetExecutorAsync("trax");
    }

    private static TrainRegistration Train<TService>(bool isQuery, string? ns) =>
        new()
        {
            ServiceType = typeof(TService),
            ImplementationType = typeof(object),
            InputType = typeof(CapInput),
            OutputType = typeof(Unit),
            Lifetime = ServiceLifetime.Scoped,
            ServiceTypeName = typeof(TService).Name,
            ImplementationTypeName = typeof(TService).Name[1..],
            HasAllowAnonymousAttribute = true,
            InputTypeName = nameof(CapInput),
            OutputTypeName = nameof(Unit),
            RequiredPolicies = [],
            RequiredRoles = [],
            IsQuery = isQuery,
            IsMutation = !isQuery,
            IsRemote = false,
            IsBroadcastEnabled = false,
            GraphQLOperations = GraphQLOperation.Run,
            GraphQLNamespace = ns,
        };

    private sealed class StubDiscovery(IReadOnlyList<TrainRegistration> trains)
        : ITrainDiscoveryService
    {
        public IReadOnlyList<TrainRegistration> DiscoverTrains() => trains;
    }

    private interface IRefundTrain;

    private interface IChargeCardTrain;

    private interface ILookupTrain;

    public record CapInput
    {
        public string Value { get; init; } = "";
    }
}
