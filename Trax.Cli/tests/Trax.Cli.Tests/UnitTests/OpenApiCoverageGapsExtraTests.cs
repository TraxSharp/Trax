using AwesomeAssertions;
using Trax.Cli.Schema.OpenApi;
using Trax.Core.Extensions;

namespace Trax.Cli.Tests.UnitTests;

[TestFixture]
public class OpenApiCoverageGapsExtraTests
{
    private static string FixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Schemas", name);

    [Test]
    public async Task Parse_SynthesizedNameCollidesWithComponent_PrefixesWithHttpVerb()
    {
        // /widgets GET has no operationId → synthesized name "Widgets" matches the
        // "Widgets" component schema, so the operation name gets the GET verb prefix.
        var parser = new OpenApiSchemaParser();
        var schema = await parser
            .ParseAsync(FixturePath("coverage-gaps.json"))
            .ConfigureAwait(false);

        schema.Operations.Should().Contain(o => o.Name == "GetWidgets");
        schema.Types.Should().Contain(t => t.Name == "Widgets");
    }

    [Test]
    public async Task Parse_OperationCollisionByPathParam_FallsBackToNumericAfterDisambiguation()
    {
        // Three operations with id "getThing" each with a single path param "thingId".
        // First wins "GetThing", second disambiguates to "GetThingByThingId",
        // third has no fresh disambiguation and falls back to numeric suffix.
        var parser = new OpenApiSchemaParser();
        var schema = await parser
            .ParseAsync(FixturePath("coverage-gaps.json"))
            .ConfigureAwait(false);

        var names = schema
            .Operations.Where(o => o.Name.StartsWith("GetThing", StringComparison.Ordinal))
            .Select(o => o.Name)
            .ToList();

        names.Should().HaveCount(3);
        names.Should().Contain("GetThing");
        names.Should().Contain("GetThingByThingId");
        names.Should().Contain(n => n.EndsWith('2'));
    }

    [Test]
    public async Task Parse_EmptyComponentReferencedDirectAndInList_RewrittenToObject()
    {
        // EmptyShape has no properties → resolved type has zero fields.
        // Direct field "single" of type EmptyShape → "object".
        // List<EmptyShape> field "many" → "List<object>".
        var parser = new OpenApiSchemaParser();
        var schema = await parser
            .ParseAsync(FixturePath("coverage-gaps.json"))
            .ConfigureAwait(false);

        var output = schema
            .Operations.Single(o => string.Equals(o.Name, "ListEmpties", StringComparison.Ordinal))
            .OutputType.NotNull();
        var single = output.Fields.Single(f =>
            string.Equals(f.Name, "Single", StringComparison.Ordinal)
        );
        var many = output.Fields.Single(f =>
            string.Equals(f.Name, "Many", StringComparison.Ordinal)
        );

        single.TypeName.Should().Be("object");
        many.TypeName.Should().Be("List<object>");
    }

    [Test]
    public async Task Parse_InlinePromotedNameCollision_GetsNumericSuffix()
    {
        // Two distinct operations each declare an inline "payload" object.
        // PromoteInlineObject → EnsureUniqueName: first "Payload", second "Payload2".
        var parser = new OpenApiSchemaParser();
        var schema = await parser
            .ParseAsync(FixturePath("coverage-gaps.json"))
            .ConfigureAwait(false);

        var typeNames = schema.Types.Select(t => t.Name).ToList();
        typeNames.Should().Contain("Payload");
        typeNames.Should().Contain("Payload2");
    }
}
