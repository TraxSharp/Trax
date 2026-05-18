using FluentAssertions;
using Trax.Api.GraphQL.Client;
using Trax.Api.Tests.GraphQLClient.IntegrationTests.Fakes;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// Mode E is meaningless if the resource loader fails to find the file. These tests pin
/// down the contract: existing resource resolves to the file's contents; missing resource
/// produces a clear error naming the resource and the assembly, not a NullReferenceException.
/// </summary>
[TestFixture]
public class ResourceLoadingTests
{
    [Test]
    public void GraphQLResourceRequest_LoadsEmbeddedQueryString()
    {
        var request = new GetPlayerByResourceRequest { Id = "ignored" };

        request.Query.Should().Contain("query GetPlayerByResource");
        request.Query.Should().Contain("inventory");
    }

    [Test]
    public void GraphQLResourceRequest_TwoInstances_ShareCachedQueryString()
    {
        var first = new GetPlayerByResourceRequest { Id = "a" };
        var second = new GetPlayerByResourceRequest { Id = "b" };

        ReferenceEquals(first.Query, second.Query)
            .Should()
            .BeTrue("the loader caches the string per type so repeated access doesn't re-read");
    }

    [Test]
    public void GraphQLResourceRequest_MissingResource_ThrowsWithDiagnosticMessage()
    {
        var request = new MissingResourceRequest();

        var act = () => _ = request.Query;

        var ex = act.Should().Throw<InvalidOperationException>();
        ex.Which.Message.Should().Contain("NonExistent.graphql");
        ex.Which.Message.Should().Contain("Trax.Api.Tests");
    }

    [Test]
    public void GraphQLQueryResourceAttribute_NullOrWhitespaceName_ThrowsArgument()
    {
        var actNull = () => new GraphQLQueryResourceAttribute(null!);
        actNull.Should().Throw<ArgumentException>();

        var actEmpty = () => new GraphQLQueryResourceAttribute("");
        actEmpty.Should().Throw<ArgumentException>();
    }
}
