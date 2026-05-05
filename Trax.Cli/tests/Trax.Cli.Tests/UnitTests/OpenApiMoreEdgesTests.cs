using FluentAssertions;
using Trax.Cli.Schema.OpenApi;

namespace Trax.Cli.Tests.UnitTests;

[TestFixture]
public class OpenApiMoreEdgesTests
{
    private static string FixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Schemas", name);

    [Test]
    public void Parse_OperationIdCollision_NoPathParams_FallsBackToNumericSuffix()
    {
        var parser = new OpenApiSchemaParser();
        var schema = parser.Parse(FixturePath("openapi-more-edges.json"));

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
    public void Parse_StringEnumComponent_BuildsApiEnum()
    {
        var parser = new OpenApiSchemaParser();
        var schema = parser.Parse(FixturePath("openapi-more-edges.json"));

        var statusEnum = schema.Enums.SingleOrDefault(e => e.Name == "Status");
        statusEnum.Should().NotBeNull();
        statusEnum!.Values.Should().BeEquivalentTo("Active", "Inactive", "Pending");
    }

    [Test]
    public void Parse_RepeatedRef_ReusesCachedType()
    {
        // Two operations both reference Status — second resolution hits the
        // _resolvedTypes/_resolvedEnums cache rather than building a fresh entry.
        var parser = new OpenApiSchemaParser();
        var schema = parser.Parse(FixturePath("openapi-more-edges.json"));

        // One enum entry total even though referenced twice.
        schema.Enums.Where(e => e.Name == "Status").Should().HaveCount(1);
    }
}
