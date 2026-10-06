using AwesomeAssertions;
using Trax.Api.GraphQL.PersistedOperations.Configuration;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

/// <summary>
/// What <c>UsePersistedOperations</c> accepts. A host must say how a change reaches every node,
/// with a broadcaster or by declaring a single node.
///
/// <para>Enforces <c>docs/adr/0026-a-persisted-operation-id-means-one-document-on-every-node.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0026-a-persisted-operation-id-means-one-document-on-every-node.md")]
[TestFixture]
public class BuilderValidationTests
{
    private const string FakeConn = "Host=fake;Database=fake";

    [Test]
    public void Build_DefaultConfig_Succeeds()
    {
        var b = new PersistedOperationsBuilder().SingleNode();
        var opts = b.Build();
        opts.RequirePersisted.Should().BeTrue();
        opts.LogNonPersistedRequests.Should().BeFalse();
        opts.AllowIntrospection.Should().BeTrue();
        opts.CacheEnabled.Should().BeFalse();
        opts.RabbitMqConnectionString.Should().BeNull();
        opts.SingleNode.Should().BeTrue();
    }

    [Test]
    public void Build_WithoutUseDatabase_Succeeds()
    {
        // Storage reads and writes through the Trax data context; no connection string is needed.
        var act = () => new PersistedOperationsBuilder().SingleNode().Build();
        act.Should().NotThrow();
    }

    [Test]
    public void Build_NeitherEnforceNorLog_Throws()
    {
        var b = new PersistedOperationsBuilder()
            .SingleNode()
            .RequirePersisted(false)
            .LogNonPersistedRequests(false);

        Action act = () => b.Build();
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*does nothing*RequirePersisted*LogNonPersistedRequests*");
    }

    [Test]
    public void Build_NeitherABroadcasterNorSingleNode_RefusesToStart()
    {
        var b = new PersistedOperationsBuilder();

        Action act = () => b.Build();
        act.Should()
            .Throw<InvalidOperationException>(
                "persisted operations refuse to start until the host says how a change reaches "
                    + "every node (Trax.Api docs/adr/0026-a-persisted-operation-id-means-one-document-on-every-node.md)"
            )
            .WithMessage("*UseRabbitMqInvalidation*SingleNode()*");
    }

    [Test]
    public void Build_BothABroadcasterAndSingleNode_RefusesToStart()
    {
        var b = new PersistedOperationsBuilder()
            .SingleNode()
            .UseRabbitMqInvalidation("amqp://localhost");

        Action act = () => b.Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*contradict*");
    }

    [Test]
    public void Build_RabbitMqWithoutTheTraxCache_Succeeds()
    {
        // The broadcast empties HotChocolate's caches too, which exist whether or not the Trax
        // lookup cache is on, so it is not tied to WithInMemoryCache.
        var opts = new PersistedOperationsBuilder()
            .UseRabbitMqInvalidation("amqp://localhost")
            .Build();

        opts.CacheEnabled.Should().BeFalse();
        opts.RabbitMqConnectionString.Should().Be("amqp://localhost");
        opts.SingleNode.Should().BeFalse();
    }

    [Test]
    public void Build_RabbitMqWithCache_Succeeds()
    {
        var opts = new PersistedOperationsBuilder()
            .WithInMemoryCache()
            .UseRabbitMqInvalidation("amqp://localhost")
            .Build();

        opts.CacheEnabled.Should().BeTrue();
        opts.RabbitMqConnectionString.Should().Be("amqp://localhost");
    }

    [Test]
    public void Build_AllowOperationsWithEmptyName_Throws()
    {
        var b = new PersistedOperationsBuilder()
            .SingleNode()
            .AllowOperations("ValidName", string.Empty);

        Action act = () => b.Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*AllowOperations*");
    }

    [Test]
    public void WithInMemoryCache_CalledTwice_Throws()
    {
        var b = new PersistedOperationsBuilder().WithInMemoryCache();
        Action act = () => b.WithInMemoryCache();
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*WithInMemoryCache*more than once*");
    }

    [Test]
    public void WithInMemoryCache_TtlOverride_Applies()
    {
        var opts = new PersistedOperationsBuilder()
            .SingleNode()
            .WithInMemoryCache(c => c.WithTtl(TimeSpan.FromMinutes(5)))
            .Build();

        opts.CacheTtl.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Test]
    public void WithInMemoryCache_NonPositiveTtl_Throws()
    {
        var b = new PersistedOperationsBuilder();
        Action act = () => b.WithInMemoryCache(c => c.WithTtl(TimeSpan.Zero));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void CacheMaxAge_DefaultsToFiveMinutes_AndTheLookupTtlFollowsIt()
    {
        var opts = new PersistedOperationsBuilder().SingleNode().WithInMemoryCache().Build();

        opts.CacheMaxAge.Should().Be(TimeSpan.FromMinutes(5));
        opts.CacheTtl.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Test]
    public void WithCacheMaxAge_Applies_AndBoundsTheDefaultTtl()
    {
        var opts = new PersistedOperationsBuilder()
            .SingleNode()
            .WithCacheMaxAge(TimeSpan.FromSeconds(30))
            .WithInMemoryCache()
            .Build();

        opts.CacheMaxAge.Should().Be(TimeSpan.FromSeconds(30));
        opts.CacheTtl.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Test]
    public void WithCacheMaxAge_NonPositive_Throws()
    {
        var b = new PersistedOperationsBuilder();
        Action act = () => b.WithCacheMaxAge(TimeSpan.Zero);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void ALookupTtlLongerThanTheMaximumAge_RefusesToStart()
    {
        var b = new PersistedOperationsBuilder()
            .SingleNode()
            .WithInMemoryCache(c => c.WithTtl(TimeSpan.FromMinutes(15)));

        Action act = () => b.Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*TTL*WithCacheMaxAge*");
    }

    [Test]
    public void UseRabbitMqInvalidation_EmptyConnString_Throws()
    {
        var b = new PersistedOperationsBuilder().WithInMemoryCache();
        Action act = () => b.UseRabbitMqInvalidation(string.Empty);
        act.Should().Throw<ArgumentException>().WithMessage("*connection string*");
    }

    /// <summary>
    /// <c>UseDatabase</c> is obsolete and chooses nothing; it still compiles and still refuses an
    /// empty string, for hosts built against it. Called by reflection, as such a host would.
    /// </summary>
    [Test]
    public void UseDatabase_IsAcceptedButChoosesNothing()
    {
        var useDatabase = typeof(PersistedOperationsBuilder).GetMethod("UseDatabase")!;
        var b = new PersistedOperationsBuilder().SingleNode();

        useDatabase.Invoke(b, ["Host=elsewhere"]).Should().BeSameAs(b);
        var empty = () => useDatabase.Invoke(b, [string.Empty]);

        empty
            .Should()
            .Throw<System.Reflection.TargetInvocationException>()
            .WithInnerException<ArgumentException>();
        typeof(PersistedOperationsOptions)
            .GetProperty("DatabaseConnectionString")!
            .GetValue(b.Build())
            .Should()
            .Be(string.Empty);
    }

    [Test]
    public void AllowOperations_NullArray_Throws()
    {
        var b = new PersistedOperationsBuilder();
        Action act = () => b.AllowOperations(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void AllowOperationsMatching_NullPredicate_Throws()
    {
        var b = new PersistedOperationsBuilder();
        Action act = () => b.AllowOperationsMatching(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void DisableIntrospection_FlipsFlag()
    {
        var opts = new PersistedOperationsBuilder().SingleNode().DisableIntrospection().Build();

        opts.AllowIntrospection.Should().BeFalse();
    }
}
