using AwesomeAssertions;
using Trax.Api.GraphQL.Client;
using Trax.Api.Tests.GraphQLClient.ResourceResolutionProbes;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// A <c>[GraphQLQueryResource]</c> name resolves to one embedded document or to none. When the
/// conventional names miss (the request's namespace does not match the folder the file is in),
/// only a resource whose name ends with the given name at a <c>.</c> boundary is a match, and more
/// than one match is refused rather than settled by manifest order.
/// </summary>
[TestFixture]
public class ResourceResolutionTests
{
    [Test]
    public void A_name_outside_the_conventional_namespace_resolves_to_the_document_it_names()
    {
        var request = new ElsewherePlayerRequest();

        request.Query.Should().Contain("query Player").And.NotContain("DeletePlayer");
    }

    [Test]
    public void A_name_two_resources_end_with_is_refused()
    {
        var request = new ElsewhereSharedRequest();

        var act = () => _ = request.Query;

        act.Should()
            .Throw<InvalidOperationException>()
            .Which.Message.Should()
            .Contain("ResourceResolution.A.Shared.graphql")
            .And.Contain("ResourceResolution.B.Shared.graphql");
    }

    [Test]
    public void A_folder_qualified_name_picks_one_of_two_same_named_resources()
    {
        var request = new ElsewhereQualifiedRequest();

        request.Query.Should().Contain("query SharedB");
    }
}
