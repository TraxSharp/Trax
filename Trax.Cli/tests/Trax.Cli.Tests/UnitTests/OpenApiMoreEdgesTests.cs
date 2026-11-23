using AwesomeAssertions;
using Trax.Cli.Schema.OpenApi;
using Trax.Core.Extensions;

namespace Trax.Cli.Tests.UnitTests;

[TestFixture]
public class OpenApiMoreEdgesTests
{
    private static string FixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Schemas", name);

    [Test]
    public async Task Parse_OperationIdCollision_NoPathParams_FallsBackToNumericSuffix()
    {
        var parser = new OpenApiSchemaParser();
        var schema = await parser
            .ParseAsync(FixturePath("openapi-more-edges.json"))
            .ConfigureAwait(false);

        // Three GET ops with operationId "listThings" and no path params force the
        // numeric fallback in EnsureUniqueOperationName.
        var listOps = schema
            .Operations.Where(o => o.Name.StartsWith("ListThings", StringComparison.Ordinal))
            .Select(o => o.Name)
            .ToList();

        listOps.Should().HaveCount(3);
        listOps.Should().Contain("ListThings");
        listOps.Should().Contain(n => n.EndsWith("2") || n.EndsWith("3"));
    }

    [Test]
    public async Task Parse_StringEnumComponent_BuildsApiEnum()
    {
        var parser = new OpenApiSchemaParser();
        var schema = await parser
            .ParseAsync(FixturePath("openapi-more-edges.json"))
            .ConfigureAwait(false);

        var statusEnum = schema.Enums.SingleOrDefault(e =>
            string.Equals(e.Name, "Status", StringComparison.Ordinal)
        );
        statusEnum.Should().NotBeNull();
        statusEnum.NotNull().Values.Should().BeEquivalentTo("Active", "Inactive", "Pending");
    }

    [Test]
    public async Task Parse_RepeatedRef_ReusesCachedType()
    {
        // Two operations both reference Status — second resolution hits the
        // _resolvedTypes/_resolvedEnums cache rather than building a fresh entry.
        var parser = new OpenApiSchemaParser();
        var schema = await parser
            .ParseAsync(FixturePath("openapi-more-edges.json"))
            .ConfigureAwait(false);

        // One enum entry total even though referenced twice.
        schema
            .Enums.Where(e => string.Equals(e.Name, "Status", StringComparison.Ordinal))
            .Should()
            .HaveCount(1);
    }
}
