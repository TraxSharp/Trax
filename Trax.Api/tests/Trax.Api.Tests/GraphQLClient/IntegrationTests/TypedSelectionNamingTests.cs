using AwesomeAssertions;
using Trax.Api.GraphQL.Client;
using Trax.Api.GraphQL.Client.Typed;
using Trax.Api.Tests.GraphQLClient.TypedSelectionProbes;

namespace Trax.Api.Tests.GraphQLClient.IntegrationTests;

/// <summary>
/// The typed client selects what an ordinary C# result type means: field names camel-cased the
/// way HotChocolate and System.Text.Json name them (<c>ID</c> is <c>id</c>), a BCL class such as
/// <see cref="Uri"/> as a scalar with no subselection, no field for an indexer, and an open
/// generic request type left out of assembly validation rather than failing it.
/// </summary>
[TestFixture]
public class TypedSelectionNamingTests
{
    private const string Sdl = """
        scalar URL
        scalar UUID
        scalar JSON

        type Query {
          asset(id: ID!): Asset
          assets(urlPath: String!, ownerID: ID): [Asset!]!
        }

        type Asset {
          id: ID!
          urlPath: String!
          link: URL
          key: UUID!
          metadata: JSON
          version: String
          parts: [Part!]!
        }

        type Part {
          name: String!
        }
        """;

    private string _path = null!;
    private GraphQLClientValidator _validator = null!;

    [SetUp]
    public async Task SetUp()
    {
        _path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"typed-{Guid.NewGuid():N}.graphql"
        );
        await File.WriteAllTextAsync(_path, Sdl);
        _validator = new GraphQLClientValidator(new FileSchemaProvider(_path));
    }

    [TearDown]
    public void TearDown() => File.Delete(_path);

    [Test]
    public async Task Acronym_property_names_are_camel_cased_as_the_server_names_them()
    {
        var query = new AssetRequest { Id = "a1" }.Query;

        query.Should().Contain(" id\n").And.Contain(" urlPath\n").And.NotContain("iD");
        (await _validator.ValidateAsync(query)).Should().Be(GraphQLParser.AST.OperationType.Query);
    }

    [Test]
    public async Task An_acronym_argument_and_root_field_are_camel_cased_too()
    {
        var query = new AssetsByURLPathRequest { URLPath = "/a" }.Query;

        query.Should().Contain("$urlPath: String!").And.Contain("assets(urlPath: $urlPath)");
        (await _validator.ValidateAsync(query)).Should().Be(GraphQLParser.AST.OperationType.Query);
    }

    [Test]
    public async Task Several_arguments_are_declared_and_passed_in_order_named_as_the_server_names_them()
    {
        var query = new AssetsByURLPathAndOwnerRequest { URLPath = "/a" }.Query;

        query
            .Should()
            .Contain("($urlPath: String!, $ownerID: ID)")
            .And.Contain("assets(urlPath: $urlPath, ownerID: $ownerID)");
        (await _validator.ValidateAsync(query)).Should().Be(GraphQLParser.AST.OperationType.Query);
    }

    [Test]
    public void A_plain_nested_class_or_an_array_of_one_is_an_object_type()
    {
        var query = new AssetRequest { Id = "a1" }.Query;

        query.Should().Contain("parts {").And.Contain(" name\n");
    }

    [Test]
    public void A_bcl_class_is_selected_as_a_scalar()
    {
        var query = new AssetRequest { Id = "a1" }.Query;

        query.Should().Contain(" link\n").And.NotContain("link {");
        query.Should().Contain(" metadata\n").And.NotContain("metadata {");
        query.Should().Contain(" version\n").And.NotContain("version {");
        query.Should().NotContain("absoluteUri");
    }

    [Test]
    public void An_indexer_selects_no_field()
    {
        var query = new AssetRequest { Id = "a1" }.Query;

        query.Should().NotContain("item");
    }

    [Test]
    public async Task An_open_generic_request_type_is_left_out_of_assembly_validation()
    {
        await _validator
            .Invoking(v =>
                v.ValidateAssembliesAsync(
                    [typeof(PagedAssetRequest<>).Assembly],
                    t => t == typeof(PagedAssetRequest<>) || t == typeof(PagedAssetsRequest)
                )
            )
            .Should()
            .NotThrowAsync();

        _validator
            .CachedQueries.Keys.Should()
            .ContainSingle("only the closed request deriving from it is validated")
            .Which.Should()
            .Contain("PagedAssets");
    }

    [Test]
    public void A_response_for_a_type_with_an_indexer_is_not_drift()
    {
        using var response = System.Text.Json.JsonDocument.Parse(
            """{"id":"a1","urlPath":"/a","link":null,"key":"5f2b","metadata":null,"version":null,"parts":[]}"""
        );

        var act = () =>
            ResponseShapeValidator.Validate(
                response.RootElement,
                typeof(AssetView),
                ResponseStrictness.ThrowOnDrift,
                new System.Text.Json.JsonSerializerOptions(),
                logger: null
            );

        act.Should().NotThrow("an indexer is not a member the response fills");
    }
}
