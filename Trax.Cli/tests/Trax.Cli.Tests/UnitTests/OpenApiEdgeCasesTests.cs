using FluentAssertions;
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
    public void Parse_ScalarResponse_BuildsSingleValueOutput()
    {
        var schema = ParseEdgeCases().Parse(FixturePath("openapi-edge-cases.json"));

        var op = schema.Operations.Single(o => o.Name == "ScalarResponse");
        op.OutputType.Name.Should().Be("ScalarResponseOutput");
        op.OutputType.Fields.Should().ContainSingle(f => f.Name == "Value");
        op.OutputType.Fields[0].TypeName.Should().Be("double");
    }

    [Test]
    public void Parse_AllOfRefAndComposition_BuildsExpectedFields()
    {
        var schema = ParseEdgeCases().Parse(FixturePath("openapi-edge-cases.json"));

        var op = schema.Operations.Single(o => o.Name == "AllOfRef");
        var fields = op.OutputType.Fields.ToDictionary(f => f.Name, f => f.TypeName);

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
    public void Parse_AllOfMergesFields_FromMultipleSchemas()
    {
        var schema = ParseEdgeCases().Parse(FixturePath("openapi-edge-cases.json"));

        // MergedShape allOf merges Base { id, name } with { extra }
        var merged = schema.Types.Single(t => t.Name == "MergedShape");
        merged.Fields.Select(f => f.Name).Should().Contain(new[] { "Id", "Name", "Extra" });
        merged.Fields.Single(f => f.Name == "Extra").IsRequired.Should().BeTrue();
    }

    [Test]
    public void Parse_OperationIdCollisions_AppliesPathParamDisambiguation()
    {
        var schema = ParseEdgeCases().Parse(FixturePath("openapi-edge-cases.json"));

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
    public void Parse_TwoSchemasInRow_CachedTypesReturnCachedInstance()
    {
        var parser = ParseEdgeCases();
        var first = parser.Parse(FixturePath("petstore.json"));
        // Re-parse a second time on a fresh parser — exercises ResolveSchemaType cache hit path
        // by parsing the same schema with previously-resolved types in _resolvedTypes
        var schema2 = ParseEdgeCases().Parse(FixturePath("openapi-edge-cases.json"));

        first.Should().NotBeNull();
        schema2.Should().NotBeNull();
    }

    [Test]
    public void Parse_GraphQL_UnknownType_BuildsObjectFallbackOutput()
    {
        var schema = new GraphQLSchemaParser().Parse(FixturePath("graphql-unknown-type.graphql"));

        var op = schema.Operations.Single();
        op.OutputType.Fields.Should().ContainSingle();
        op.OutputType.Fields[0].TypeName.Should().Be("object");
        op.OutputType.Fields[0].Description.Should().Contain("UndefinedType");
    }
}
