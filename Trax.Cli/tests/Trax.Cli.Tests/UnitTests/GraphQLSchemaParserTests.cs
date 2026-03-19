using FluentAssertions;
using Trax.Cli.Models;
using Trax.Cli.Schema.GraphQL;

namespace Trax.Cli.Tests.UnitTests;

[TestFixture]
public class GraphQLSchemaParserTests
{
    private GraphQLSchemaParser _parser = null!;

    [SetUp]
    public void SetUp()
    {
        _parser = new GraphQLSchemaParser();
    }

    private static string FixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Schemas", name);

    #region SimpleSchema

    [Test]
    public void Parse_SimpleGraphql_HasTwoOperations()
    {
        var schema = _parser.Parse(FixturePath("simple.graphql"));

        schema.Operations.Should().HaveCount(2);
        schema.Operations.Should().Contain(o => o.Kind == OperationKind.Query);
        schema.Operations.Should().Contain(o => o.Kind == OperationKind.Mutation);
    }

    [Test]
    public void Parse_SimpleGraphql_HasQueryNamedGetPlayer()
    {
        var schema = _parser.Parse(FixturePath("simple.graphql"));

        var query = schema.Operations.First(o => o.Kind == OperationKind.Query);
        query.Name.Should().Be("GetPlayer");
    }

    [Test]
    public void Parse_SimpleGraphql_HasMutationNamedCreatePlayer()
    {
        var schema = _parser.Parse(FixturePath("simple.graphql"));

        var mutation = schema.Operations.First(o => o.Kind == OperationKind.Mutation);
        mutation.Name.Should().Be("CreatePlayer");
    }

    [Test]
    public void Parse_SimpleGraphql_HasPlayerTypeWithThreeFields()
    {
        var schema = _parser.Parse(FixturePath("simple.graphql"));

        schema.Types.Should().Contain(t => t.Name == "Player");
        var player = schema.Types.First(t => t.Name == "Player");
        player.Fields.Should().HaveCount(3);
    }

    [Test]
    public void Parse_SimpleGraphql_QueryInputTypeHasIdFieldMappedToString()
    {
        var schema = _parser.Parse(FixturePath("simple.graphql"));

        var query = schema.Operations.First(o => o.Kind == OperationKind.Query);
        query.InputType.Fields.Should().HaveCount(1);

        var idField = query.InputType.Fields[0];
        idField.Name.Should().Be("Id");
        idField.TypeName.Should().Be("string");
    }

    [Test]
    public void Parse_SimpleGraphql_MutationInputTypeHasTwoRequiredFields()
    {
        var schema = _parser.Parse(FixturePath("simple.graphql"));

        var mutation = schema.Operations.First(o => o.Kind == OperationKind.Mutation);
        mutation.InputType.Fields.Where(f => f.IsRequired).Should().HaveCount(2);
    }

    #endregion

    #region NestedTypes

    [Test]
    public void Parse_NestedTypes_ContainsOrderOrderItemCustomer()
    {
        var schema = _parser.Parse(FixturePath("nested-types.graphql"));

        var typeNames = schema.Types.Select(t => t.Name).ToList();
        typeNames.Should().Contain("Order");
        typeNames.Should().Contain("OrderItem");
        typeNames.Should().Contain("Customer");
    }

    #endregion

    #region Enums

    [Test]
    public void Parse_Enums_HasOneEnumWithFourValues()
    {
        var schema = _parser.Parse(FixturePath("enums.graphql"));

        schema.Enums.Should().HaveCount(1);
        var statusEnum = schema.Enums[0];
        statusEnum.Name.Should().Be("Status");
        statusEnum.Values.Should().HaveCount(4);
    }

    [Test]
    public void Parse_Enums_HasMutationNamedUpdateStatus()
    {
        var schema = _parser.Parse(FixturePath("enums.graphql"));

        schema
            .Operations.Should()
            .Contain(o => o.Kind == OperationKind.Mutation && o.Name == "UpdateStatus");
    }

    #endregion

    #region Nullable

    [Test]
    public void Parse_Nullable_QuerySearchHasThreeNullableInputFields()
    {
        var schema = _parser.Parse(FixturePath("nullable.graphql"));

        var search = schema.Operations.First(o => o.Name == "Search");
        search.InputType.Fields.Should().HaveCount(3);
        search.InputType.Fields.Should().AllSatisfy(f => f.IsNullable.Should().BeTrue());
    }

    [Test]
    public void Parse_Nullable_SearchResultHasNullableFields()
    {
        var schema = _parser.Parse(FixturePath("nullable.graphql"));

        var searchResult = schema.Types.First(t => t.Name == "SearchResult");

        var items = searchResult.Fields.First(f => f.Name == "Items");
        items.IsNullable.Should().BeTrue();

        var nextCursor = searchResult.Fields.First(f => f.Name == "NextCursor");
        nextCursor.IsNullable.Should().BeTrue();
    }

    #endregion

    #region TypeCollision

    [Test]
    public void Parse_TypeCollision_AllChatsQueryIsDisambiguated()
    {
        var schema = _parser.Parse(FixturePath("type-collision.graphql"));

        schema.Operations.Should().Contain(o => o.Name == "AllChatsQuery");
    }

    [Test]
    public void Parse_TypeCollision_ChatHistoriesQueryIsDisambiguated()
    {
        var schema = _parser.Parse(FixturePath("type-collision.graphql"));

        schema.Operations.Should().Contain(o => o.Name == "ChatHistoriesQuery");
    }

    [Test]
    public void Parse_TypeCollision_NonCollidingOperationNamesUnchanged()
    {
        var schema = _parser.Parse(FixturePath("type-collision.graphql"));

        // GetPlayer doesn't collide with any type name
        schema.Operations.Should().Contain(o => o.Name == "GetPlayer");
        // CreateChat doesn't collide with Chat (different name)
        schema.Operations.Should().Contain(o => o.Name == "CreateChat");
    }

    [Test]
    public void Parse_TypeCollision_DisambiguatedOperationOutputTypePreserved()
    {
        var schema = _parser.Parse(FixturePath("type-collision.graphql"));

        var allChats = schema.Operations.First(o => o.Name == "AllChatsQuery");
        // Output references the AllChats type (built-in ref since it's a known object type)
        allChats.OutputType.Name.Should().Be("AllChats");
        allChats.OutputType.IsBuiltIn.Should().BeTrue();
    }

    [Test]
    public void Parse_TypeCollision_MutationCollidingWithTypeGetsMutationSuffix()
    {
        // Create a schema where a mutation name collides with a type
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
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
            var schema = _parser.Parse(tempFile);

            schema.Operations.Should().Contain(o => o.Name == "ItemMutation");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public void Parse_TypeCollision_CollidingWithEnumTypeIsDisambiguated()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
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
            var schema = _parser.Parse(tempFile);

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
    public void Parse_TypeCollision_CollidingWithInputTypeIsDisambiguated()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
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
            var schema = _parser.Parse(tempFile);

            // "FilterInput" collides with the FilterInput input type
            schema.Operations.Should().Contain(o => o.Name == "FilterInputQuery");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public void Parse_TypeCollision_HasCorrectTotalOperationCount()
    {
        var schema = _parser.Parse(FixturePath("type-collision.graphql"));

        schema.Operations.Should().HaveCount(4);
    }

    #endregion

    #region EmptySchema

    [Test]
    public void Parse_EmptySchema_HasZeroOperations()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, string.Empty);
            var schema = _parser.Parse(tempFile);
            schema.Operations.Should().BeEmpty();
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion
}
