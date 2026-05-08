using FluentAssertions;
using Trax.Api.GraphQL.PersistedOperations.Configuration;

namespace Trax.Api.Tests.PersistedOperations.UnitTests;

[TestFixture]
public class BuilderValidationTests
{
    private const string FakeConn = "Host=fake;Database=fake";

    [Test]
    public void Build_DefaultConfig_Succeeds()
    {
        var b = new PersistedOperationsBuilder().UseDatabase(FakeConn);
        var opts = b.Build();
        opts.RequirePersisted.Should().BeTrue();
        opts.LogNonPersistedRequests.Should().BeFalse();
        opts.AllowIntrospection.Should().BeTrue();
        opts.CacheEnabled.Should().BeFalse();
        opts.RabbitMqConnectionString.Should().BeNull();
    }

    [Test]
    public void Build_NoDatabase_Throws()
    {
        var b = new PersistedOperationsBuilder();
        Action act = () => b.Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*UseDatabase*");
    }

    [Test]
    public void Build_NeitherEnforceNorLog_Throws()
    {
        var b = new PersistedOperationsBuilder()
            .UseDatabase(FakeConn)
            .RequirePersisted(false)
            .LogNonPersistedRequests(false);

        Action act = () => b.Build();
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*does nothing*RequirePersisted*LogNonPersistedRequests*");
    }

    [Test]
    public void Build_RabbitMqWithoutCache_Throws()
    {
        var b = new PersistedOperationsBuilder()
            .UseDatabase(FakeConn)
            .UseRabbitMqInvalidation("amqp://localhost");

        Action act = () => b.Build();
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*UseRabbitMqInvalidation*WithInMemoryCache*");
    }

    [Test]
    public void Build_RabbitMqWithCache_Succeeds()
    {
        var opts = new PersistedOperationsBuilder()
            .UseDatabase(FakeConn)
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
            .UseDatabase(FakeConn)
            .AllowOperations("ValidName", string.Empty);

        Action act = () => b.Build();
        act.Should().Throw<InvalidOperationException>().WithMessage("*AllowOperations*");
    }

    [Test]
    public void WithInMemoryCache_CalledTwice_Throws()
    {
        var b = new PersistedOperationsBuilder().UseDatabase(FakeConn).WithInMemoryCache();
        Action act = () => b.WithInMemoryCache();
        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*WithInMemoryCache*more than once*");
    }

    [Test]
    public void WithInMemoryCache_TtlOverride_Applies()
    {
        var opts = new PersistedOperationsBuilder()
            .UseDatabase(FakeConn)
            .WithInMemoryCache(c => c.WithTtl(TimeSpan.FromMinutes(5)))
            .Build();

        opts.CacheTtl.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Test]
    public void WithInMemoryCache_NonPositiveTtl_Throws()
    {
        var b = new PersistedOperationsBuilder().UseDatabase(FakeConn);
        Action act = () => b.WithInMemoryCache(c => c.WithTtl(TimeSpan.Zero));
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void UseRabbitMqInvalidation_EmptyConnString_Throws()
    {
        var b = new PersistedOperationsBuilder().UseDatabase(FakeConn).WithInMemoryCache();
        Action act = () => b.UseRabbitMqInvalidation(string.Empty);
        act.Should().Throw<ArgumentException>().WithMessage("*connection string*");
    }

    [Test]
    public void UseDatabase_EmptyConnString_Throws()
    {
        var b = new PersistedOperationsBuilder();
        Action act = () => b.UseDatabase(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void AllowOperations_NullArray_Throws()
    {
        var b = new PersistedOperationsBuilder().UseDatabase(FakeConn);
        Action act = () => b.AllowOperations(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void AllowOperationsMatching_NullPredicate_Throws()
    {
        var b = new PersistedOperationsBuilder().UseDatabase(FakeConn);
        Action act = () => b.AllowOperationsMatching(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void DisableIntrospection_FlipsFlag()
    {
        var opts = new PersistedOperationsBuilder()
            .UseDatabase(FakeConn)
            .DisableIntrospection()
            .Build();

        opts.AllowIntrospection.Should().BeFalse();
    }
}
