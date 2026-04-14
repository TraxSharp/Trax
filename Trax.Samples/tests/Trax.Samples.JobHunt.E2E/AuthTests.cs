using FluentAssertions;
using NUnit.Framework;
using Trax.Samples.JobHunt.E2E.Fixtures;

namespace Trax.Samples.JobHunt.E2E;

[TestFixture]
public class AuthTests : JobHuntApiTestFixture
{
    private const string IntrospectionQuery = "{ __schema { queryType { name } } }";

    [Test]
    public async Task GraphQL_NoApiKey_StillReturnsSchema()
    {
        // Phase 0: GraphQL endpoint itself does not yet require auth (no [Authorize]
        // attributes on resolvers, no global filter). Once trains are added in Phase 1
        // they will use [TraxQuery]/[TraxMutation] which inherit endpoint auth.
        // For Phase 0 we assert the introspection query works without an API key.
        var result = await GraphQL.SendAsync(IntrospectionQuery);

        result.HasErrors.Should().BeFalse();
        result.GetData("__schema", "queryType", "name").GetString().Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task GraphQL_ValidApiKey_Returns200()
    {
        var result = await GraphQL.SendAsync(IntrospectionQuery, apiKey: AliceKey);

        result.HasErrors.Should().BeFalse();
    }

    [Test]
    public async Task GraphQL_BogusApiKey_StillReturnsSchema_BecauseAuthIsNotEnforcedOnIntrospection()
    {
        // The auth handler returns a Failure for unknown keys, but ASP.NET Core does not
        // automatically reject the request unless an [Authorize] attribute applies. With
        // no authorization policies enforced in Phase 0, the request still completes.
        // This test documents that behavior so future phases that add [Authorize]
        // can update it explicitly.
        var result = await GraphQL.SendAsync(IntrospectionQuery, apiKey: "bogus-key");

        result.HasErrors.Should().BeFalse();
    }
}
