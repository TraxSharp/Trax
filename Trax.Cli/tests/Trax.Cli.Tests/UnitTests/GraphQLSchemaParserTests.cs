using AwesomeAssertions;
using Trax.Cli.Models;
using Trax.Cli.Schema.GraphQL;

namespace Trax.Cli.Tests.UnitTests;

[TestFixture]
public class GraphQLSchemaParserTests
{
    private GraphQLSchemaParser _parser = new();

    [SetUp]
    public void SetUp()
    {
        _parser = new GraphQLSchemaParser();
    }

    private static string FixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Schemas", name);

    #region SimpleSchema

    [Test]
    public async Task Parse_SimpleGraphql_HasTwoOperations()
    {
        var schema = await _parser.ParseAsync(FixturePath("simple.graphql")).ConfigureAwait(false);

        schema.Operations.Should().HaveCount(2);
        schema.Operations.Should().Contain(o => o.Kind == OperationKind.Query);
        schema.Operations.Should().Contain(o => o.Kind == OperationKind.Mutation);
    }

    [Test]
    public async Task Parse_SimpleGraphql_HasQueryNamedGetPlayer()
    {
        var schema = await _parser.ParseAsync(FixturePath("simple.graphql")).ConfigureAwait(false);

        var query = schema.Operations.First(o => o.Kind == OperationKind.Query);
        query.Name.Should().Be("GetPlayer");
    }

    [Test]
    public async Task Parse_SimpleGraphql_HasMutationNamedCreatePlayer()
    {
        var schema = await _parser.ParseAsync(FixturePath("simple.graphql")).ConfigureAwait(false);

        var mutation = schema.Operations.First(o => o.Kind == OperationKind.Mutation);
        mutation.Name.Should().Be("CreatePlayer");
    }

    [Test]
    public async Task Parse_SimpleGraphql_HasPlayerTypeWithThreeFields()
    {
        var schema = await _parser.ParseAsync(FixturePath("simple.graphql")).ConfigureAwait(false);

        schema.Types.Should().Contain(t => t.Name == "Player");
        var player = schema.Types.First(t =>
            string.Equals(t.Name, "Player", StringComparison.Ordinal)
        );
        player.Fields.Should().HaveCount(3);
    }

    [Test]
    public async Task Parse_SimpleGraphql_QueryInputTypeHasIdFieldMappedToString()
    {
        var schema = await _parser.ParseAsync(FixturePath("simple.graphql")).ConfigureAwait(false);

        var query = schema.Operations.First(o => o.Kind == OperationKind.Query);
        query.InputType.Fields.Should().HaveCount(1);

        var idField = query.InputType.Fields[0];
        idField.Name.Should().Be("Id");
        idField.TypeName.Should().Be("string");
    }

    [Test]
    public async Task Parse_SimpleGraphql_MutationInputTypeHasTwoRequiredFields()
    {
        var schema = await _parser.ParseAsync(FixturePath("simple.graphql")).ConfigureAwait(false);

        var mutation = schema.Operations.First(o => o.Kind == OperationKind.Mutation);
        mutation.InputType.Fields.Where(f => f.IsRequired).Should().HaveCount(2);
    }

    #endregion

    #region NestedTypes

    [Test]
    public async Task Parse_NestedTypes_ContainsOrderOrderItemCustomer()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("nested-types.graphql"))
            .ConfigureAwait(false);

        var typeNames = schema.Types.Select(t => t.Name).ToList();
        typeNames.Should().Contain("Order");
        typeNames.Should().Contain("OrderItem");
        typeNames.Should().Contain("Customer");
    }

    #endregion

    #region Enums

    [Test]
    public async Task Parse_Enums_HasOneEnumWithFourValues()
    {
        var schema = await _parser.ParseAsync(FixturePath("enums.graphql")).ConfigureAwait(false);

        schema.Enums.Should().HaveCount(1);
        var statusEnum = schema.Enums[0];
        statusEnum.Name.Should().Be("Status");
        statusEnum.Values.Should().HaveCount(4);
    }

    [Test]
    public async Task Parse_Enums_HasMutationNamedUpdateStatus()
    {
        var schema = await _parser.ParseAsync(FixturePath("enums.graphql")).ConfigureAwait(false);

        schema
            .Operations.Should()
            .Contain(o => o.Kind == OperationKind.Mutation && o.Name == "UpdateStatus");
    }

    #endregion

    #region Nullable

    [Test]
    public async Task Parse_Nullable_QuerySearchHasThreeNullableInputFields()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("nullable.graphql"))
            .ConfigureAwait(false);

        var search = schema.Operations.First(o =>
            string.Equals(o.Name, "Search", StringComparison.Ordinal)
        );
        search.InputType.Fields.Should().HaveCount(3);
        search.InputType.Fields.Should().AllSatisfy(f => f.IsNullable.Should().BeTrue());
    }

    [Test]
    public async Task Parse_Nullable_SearchResultHasNullableFields()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("nullable.graphql"))
            .ConfigureAwait(false);

        var searchResult = schema.Types.First(t =>
            string.Equals(t.Name, "SearchResult", StringComparison.Ordinal)
        );

        var items = searchResult.Fields.First(f =>
            string.Equals(f.Name, "Items", StringComparison.Ordinal)
        );
        items.IsNullable.Should().BeTrue();

        var nextCursor = searchResult.Fields.First(f =>
            string.Equals(f.Name, "NextCursor", StringComparison.Ordinal)
        );
        nextCursor.IsNullable.Should().BeTrue();
    }

    #endregion

    #region TypeCollision

    [Test]
    public async Task Parse_TypeCollision_AllChatsQueryIsDisambiguated()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("type-collision.graphql"))
            .ConfigureAwait(false);

        schema.Operations.Should().Contain(o => o.Name == "AllChatsQuery");
    }

    [Test]
    public async Task Parse_TypeCollision_ChatHistoriesQueryIsDisambiguated()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("type-collision.graphql"))
            .ConfigureAwait(false);

        schema.Operations.Should().Contain(o => o.Name == "ChatHistoriesQuery");
    }

    [Test]
    public async Task Parse_TypeCollision_NonCollidingOperationNamesUnchanged()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("type-collision.graphql"))
            .ConfigureAwait(false);

        // GetPlayer doesn't collide with any type name
        schema.Operations.Should().Contain(o => o.Name == "GetPlayer");
        // CreateChat doesn't collide with Chat (different name)
        schema.Operations.Should().Contain(o => o.Name == "CreateChat");
    }

    [Test]
    public async Task Parse_TypeCollision_DisambiguatedOperationOutputTypePreserved()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("type-collision.graphql"))
            .ConfigureAwait(false);

        var allChats = schema.Operations.First(o =>
            string.Equals(o.Name, "AllChatsQuery", StringComparison.Ordinal)
        );
        // Output references the AllChats type (built-in ref since it's a known object type)
        allChats.OutputType.Name.Should().Be("AllChats");
        allChats.OutputType.IsBuiltIn.Should().BeTrue();
    }

    [Test]
    public async Task Parse_TypeCollision_MutationCollidingWithTypeGetsMutationSuffix()
    {
        // Create a schema where a mutation name collides with a type
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(
                tempFile,
                """
                type Query {
                  getItem(id: ID!): Item
                }
                type Mutation {
                  item(name: String!): Item
                }
                type Item {
                  id: ID!
                  name: String!
                }
                """
            );
            var schema = await _parser.ParseAsync(tempFile).ConfigureAwait(false);

            schema.Operations.Should().Contain(o => o.Name == "ItemMutation");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task Parse_TypeCollision_CollidingWithEnumTypeIsDisambiguated()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(
                tempFile,
                """
                type Query {
                  status: StatusResult!
                }
                type Mutation {
                  status(value: Status!): StatusResult!
                }
                type StatusResult {
                  ok: Boolean!
                }
                enum Status {
                  ACTIVE
                  INACTIVE
                }
                """
            );
            var schema = await _parser.ParseAsync(tempFile).ConfigureAwait(false);

            // "Status" collides with the Status enum
            schema.Operations.Should().Contain(o => o.Name == "StatusMutation");
            // The query "status" also collides with the Status enum
            schema.Operations.Should().Contain(o => o.Name == "StatusQuery");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task Parse_TypeCollision_CollidingWithInputTypeIsDisambiguated()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(
                tempFile,
                """
                type Query {
                  filterInput(text: String): SearchResult
                }
                type SearchResult {
                  items: [String!]!
                }
                input FilterInput {
                  field: String
                  value: String
                }
                """
            );
            var schema = await _parser.ParseAsync(tempFile).ConfigureAwait(false);

            // "FilterInput" collides with the FilterInput input type
            schema.Operations.Should().Contain(o => o.Name == "FilterInputQuery");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task Parse_TypeCollision_HasCorrectTotalOperationCount()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("type-collision.graphql"))
            .ConfigureAwait(false);

        schema.Operations.Should().HaveCount(4);
    }

    #endregion

    #region EmptySchema

    [Test]
    public async Task Parse_EmptySchema_HasZeroOperations()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, string.Empty);
            var schema = await _parser.ParseAsync(tempFile).ConfigureAwait(false);
            schema.Operations.Should().BeEmpty();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion
}
