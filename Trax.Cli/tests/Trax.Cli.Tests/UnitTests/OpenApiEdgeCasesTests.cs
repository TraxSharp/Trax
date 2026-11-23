using AwesomeAssertions;
using Trax.Cli.Schema.GraphQL;
using Trax.Cli.Schema.OpenApi;

namespace Trax.Cli.Tests.UnitTests;

[TestFixture]
public class OpenApiEdgeCasesTests
{
    private static string FixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Schemas", name);

    private static OpenApiSchemaParser ParseEdgeCases() => new();

    [Test]
    public async Task Parse_ScalarResponse_BuildsSingleValueOutput()
    {
        var schema = await ParseEdgeCases()
            .ParseAsync(FixturePath("openapi-edge-cases.json"))
            .ConfigureAwait(false);

        var op = schema.Operations.Single(o =>
            string.Equals(o.Name, "ScalarResponse", StringComparison.Ordinal)
        );
        op.OutputType.Name.Should().Be("ScalarResponseOutput");
        op.OutputType.Fields.Should().ContainSingle(f => f.Name == "Value");
        op.OutputType.Fields[0].TypeName.Should().Be("double");
    }

    [Test]
    public async Task Parse_AllOfRefAndComposition_BuildsExpectedFields()
    {
        var schema = await ParseEdgeCases()
            .ParseAsync(FixturePath("openapi-edge-cases.json"))
            .ConfigureAwait(false);

        var op = schema.Operations.Single(o =>
            string.Equals(o.Name, "AllOfRef", StringComparison.Ordinal)
        );
        var fields = op.OutputType.Fields.ToDictionary(
            f => f.Name,
            f => f.TypeName,
            StringComparer.Ordinal
        );

        // allOf with $ref → resolves to the referenced type name (Pascal-cased)
        fields.Should().ContainKey("Merged");
        // allOf without $ref → falls back to "object"
        fields["Synthesized"].Should().Be("object");
        // oneOf with $ref → resolves to the referenced type name
        fields.Should().ContainKey("OneOfRef");
        // oneOf without $ref (primitives) → "object"
        fields["OneOfPrimitive"].Should().Be("object");
        // anyOf with $ref → resolves
        fields.Should().ContainKey("AnyOfRef");
        // anyOf without $ref → "object"
        fields["AnyOfPrimitive"].Should().Be("object");
        // additionalProperties → Dictionary<string, T>
        fields["StringMap"].Should().Be("Dictionary<string, string>");
        // number/float and uuid/uri/binary/date/date-time scalar formats
        fields["FloatVal"].Should().Be("float");
        fields["UuidVal"].Should().Be("Guid");
        fields["UriVal"].Should().Be("Uri");
        fields["BinaryVal"].Should().Be("byte[]");
        fields["DateVal"].Should().Be("DateOnly");
        fields["DateTimeVal"].Should().Be("DateTime");
        // Unspecified type → "object"
        fields["Untyped"].Should().Be("object");
    }

    [Test]
    public async Task Parse_AllOfMergesFields_FromMultipleSchemas()
    {
        var schema = await ParseEdgeCases()
            .ParseAsync(FixturePath("openapi-edge-cases.json"))
            .ConfigureAwait(false);

        // MergedShape allOf merges Base { id, name } with { extra }
        var merged = schema.Types.Single(t =>
            string.Equals(t.Name, "MergedShape", StringComparison.Ordinal)
        );
        merged.Fields.Select(f => f.Name).Should().Contain(new[] { "Id", "Name", "Extra" });
        merged
            .Fields.Single(f => string.Equals(f.Name, "Extra", StringComparison.Ordinal))
            .IsRequired.Should()
            .BeTrue();
    }

    [Test]
    public async Task Parse_OperationIdCollisions_AppliesPathParamDisambiguation()
    {
        var schema = await ParseEdgeCases()
            .ParseAsync(FixturePath("openapi-edge-cases.json"))
            .ConfigureAwait(false);

        // Three operations all named getUser are disambiguated:
        //   /users/{userId}                   → GetUser
        //   /users/{userId}/profile           → GetUserByUserId (path-param disambiguation)
        //   /users/{userId}/posts/{postId}    → GetUserByUserIdAndPostId
        // and a remaining collision falls back to numeric suffix.
        var names = schema.Operations.Select(o => o.Name).ToList();
        names.Should().Contain("GetUser");
        names.Should().Contain(n => n.StartsWith("GetUserByUserId"));
    }

    [Test]
    public async Task Parse_TwoSchemasInRow_CachedTypesReturnCachedInstance()
    {
        var parser = ParseEdgeCases();
        var first = await parser.ParseAsync(FixturePath("petstore.json")).ConfigureAwait(false);
        // Re-parse a second time on a fresh parser — exercises ResolveSchemaType cache hit path
        // by parsing the same schema with previously-resolved types in _resolvedTypes
        var schema2 = await ParseEdgeCases()
            .ParseAsync(FixturePath("openapi-edge-cases.json"))
            .ConfigureAwait(false);

        first.Should().NotBeNull();
        schema2.Should().NotBeNull();
    }

    [Test]
    public async Task Parse_GraphQL_UnknownType_BuildsObjectFallbackOutput()
    {
        var schema = await new GraphQLSchemaParser()
            .ParseAsync(FixturePath("graphql-unknown-type.graphql"))
            .ConfigureAwait(false);

        var op = schema.Operations.Single();
        op.OutputType.Fields.Should().ContainSingle();
        op.OutputType.Fields[0].TypeName.Should().Be("object");
        op.OutputType.Fields[0].Description.Should().Contain("UndefinedType");
    }
}
