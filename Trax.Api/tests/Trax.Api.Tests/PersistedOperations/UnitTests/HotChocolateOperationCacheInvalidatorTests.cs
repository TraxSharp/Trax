using FluentAssertions;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Trax.Api.GraphQL.PersistedOperations.Storage;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

/// <summary>
/// The invalidator runs on the tail of an operator's upsert. Anything it throws would
/// surface as a failed upsert of a document that was in fact stored, so its contract is
/// that only cancellation escapes.
/// </summary>
[TestFixture]
public class HotChocolateOperationCacheInvalidatorTests
{
    [Test]
    public async Task InvalidateAsync_NoGraphQLServer_IsANoOp()
    {
        // A host that stores persisted operations without a schema in-process: there is no
        // executor to reach, and that is not an error.
        var invalidator = Build(new ServiceCollection().BuildServiceProvider());

        var act = () => invalidator.InvalidateAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task InvalidateAsync_ExecutorLookupThrows_IsSwallowed()
    {
        // A schema that cannot currently be composed must not fail the upsert that
        // triggered this.
        var provider = Substitute.For<IRequestExecutorProvider>();
        provider
            .GetExecutorAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("schema is broken"));

        var services = new ServiceCollection();
        services.AddSingleton(provider);
        var invalidator = Build(services.BuildServiceProvider());

        var act = () => invalidator.InvalidateAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task InvalidateAsync_PreCancelledToken_Throws()
    {
        var invalidator = Build(new ServiceCollection().BuildServiceProvider());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => invalidator.InvalidateAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task InvalidateAsync_CancelledDuringLookup_PropagatesCancellation()
    {
        // Cancellation is the one thing that must not be swallowed: the caller needs to
        // know the invalidation did not happen.
        var provider = Substitute.For<IRequestExecutorProvider>();
        provider
            .GetExecutorAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var services = new ServiceCollection();
        services.AddSingleton(provider);
        var invalidator = Build(services.BuildServiceProvider());

        var act = () => invalidator.InvalidateAsync(CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task InvalidateAsync_SchemaWithoutTraxCaches_IsANoOp()
    {
        // A host that wires GraphQL but not the persisted-operations package keeps
        // HotChocolate's own caches, which the invalidator leaves alone.
        var services = new ServiceCollection();
        services
            .AddGraphQLServer()
            .AddQueryType(d => d.Name("Query").Field("ping").Type<StringType>().Resolve("pong"));
        var provider = services.BuildServiceProvider();
        var invalidator = Build(provider);
        invalidator.SetSchemaName(null);

        var act = () => invalidator.InvalidateAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Test]
    public void Constructor_NullArguments_Throw()
    {
        var services = new ServiceCollection().BuildServiceProvider();

        var nullServices = () =>
            new HotChocolateOperationCacheInvalidator(
                null!,
                NullLogger<HotChocolateOperationCacheInvalidator>.Instance
            );
        var nullLogger = () => new HotChocolateOperationCacheInvalidator(services, null!);

        nullServices.Should().Throw<ArgumentNullException>();
        nullLogger.Should().Throw<ArgumentNullException>();
    }

    private static HotChocolateOperationCacheInvalidator Build(IServiceProvider services) =>
        new(services, NullLogger<HotChocolateOperationCacheInvalidator>.Instance);
}
