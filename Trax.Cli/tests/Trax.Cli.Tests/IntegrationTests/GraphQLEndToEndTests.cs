using AwesomeAssertions;
using Trax.Cli.Generator;
using Trax.Cli.Schema.GraphQL;

namespace Trax.Cli.Tests.IntegrationTests;

[TestFixture]
public class GraphQLEndToEndTests
{
    private GraphQLSchemaParser _parser = new();
    private TraxProjectGenerator _generator = new();
    private string _outputDir = "";

    [SetUp]
    public void SetUp()
    {
        _parser = new GraphQLSchemaParser();
        _generator = new TraxProjectGenerator();
        _outputDir = Path.Combine(Path.GetTempPath(), $"trax-cli-e2e-{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, recursive: true);
    }

    private static string FixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Schemas", name);

    #region SimpleSchema

    [Test]
    public async Task GenerateTrainsLibrary_SimpleGraphql_AllExpectedFilesExist()
    {
        var schema = await _parser.ParseAsync(FixturePath("simple.graphql")).ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        // Trains library files
        File.Exists(Path.Combine(_outputDir, "TestProject.Trains.csproj")).Should().BeTrue();
        File.Exists(Path.Combine(_outputDir, "ManifestNames.cs")).Should().BeTrue();

        // GetPlayer query train files
        var getPlayerDir = Path.Combine(_outputDir, "Trains", "Players", "GetPlayer");
        File.Exists(Path.Combine(getPlayerDir, "IGetPlayerTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(getPlayerDir, "GetPlayerTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(getPlayerDir, "GetPlayerInput.cs")).Should().BeTrue();
        File.Exists(Path.Combine(getPlayerDir, "Junctions", "GetPlayerJunction.cs"))
            .Should()
            .BeTrue();

        // CreatePlayer mutation train files
        var createPlayerDir = Path.Combine(_outputDir, "Trains", "Players", "CreatePlayer");
        File.Exists(Path.Combine(createPlayerDir, "ICreatePlayerTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(createPlayerDir, "CreatePlayerTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(createPlayerDir, "CreatePlayerInput.cs")).Should().BeTrue();
        File.Exists(Path.Combine(createPlayerDir, "Junctions", "CreatePlayerJunction.cs"))
            .Should()
            .BeTrue();
    }

    [Test]
    public async Task GenerateTrainsLibrary_SimpleGraphql_ManifestNamesContainsOperations()
    {
        var schema = await _parser.ParseAsync(FixturePath("simple.graphql")).ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var content = await File.ReadAllTextAsync(Path.Combine(_outputDir, "ManifestNames.cs"))
            .ConfigureAwait(false);

        content.Should().Contain("GetPlayer");
        content.Should().Contain("CreatePlayer");
        content.Should().Contain("\"get-player\"");
        content.Should().Contain("\"create-player\"");
    }

    [Test]
    public async Task GenerateTrainsLibrary_SimpleGraphql_QueryTrainHasTraxQueryAttribute()
    {
        var schema = await _parser.ParseAsync(FixturePath("simple.graphql")).ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var trainFile = Path.Combine(
            _outputDir,
            "Trains",
            "Players",
            "GetPlayer",
            "GetPlayerTrain.cs"
        );
        var content = await File.ReadAllTextAsync(trainFile).ConfigureAwait(false);

        content.Should().Contain("[TraxQuery");
    }

    [Test]
    public async Task GenerateTrainsLibrary_SimpleGraphql_MutationTrainHasTraxMutationAttribute()
    {
        var schema = await _parser.ParseAsync(FixturePath("simple.graphql")).ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var trainFile = Path.Combine(
            _outputDir,
            "Trains",
            "Players",
            "CreatePlayer",
            "CreatePlayerTrain.cs"
        );
        var content = await File.ReadAllTextAsync(trainFile).ConfigureAwait(false);

        content.Should().Contain("[TraxMutation");
    }

    #endregion

    #region NestedTypes

    [Test]
    public async Task GenerateTrainsLibrary_NestedTypesGraphql_ModelsContainsSharedTypes()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("nested-types.graphql"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var modelsDir = Path.Combine(_outputDir, "Models");
        File.Exists(Path.Combine(modelsDir, "Order.cs")).Should().BeTrue();
        File.Exists(Path.Combine(modelsDir, "OrderItem.cs")).Should().BeTrue();
        File.Exists(Path.Combine(modelsDir, "Customer.cs")).Should().BeTrue();
    }

    #endregion

    #region Enums

    [Test]
    public async Task GenerateTrainsLibrary_EnumsGraphql_ModelsContainsStatusEnum()
    {
        var schema = await _parser.ParseAsync(FixturePath("enums.graphql")).ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var enumFile = Path.Combine(_outputDir, "Models", "Status.cs");
        File.Exists(enumFile).Should().BeTrue();

        var content = await File.ReadAllTextAsync(enumFile).ConfigureAwait(false);
        content.Should().Contain("PENDING");
        content.Should().Contain("ACTIVE");
        content.Should().Contain("COMPLETED");
        content.Should().Contain("CANCELLED");
    }

    #endregion

    #region TypeCollision

    [Test]
    public async Task GenerateTrainsLibrary_TypeCollision_DisambiguatedOperationDirsExist()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("type-collision.graphql"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        // AllChats query collides with AllChats type → AllChatsQuery
        var allChatsDir = Path.Combine(_outputDir, "Trains", "AllChats", "AllChatsQuery");
        Directory.Exists(allChatsDir).Should().BeTrue();
        File.Exists(Path.Combine(allChatsDir, "IAllChatsQueryTrain.cs")).Should().BeTrue();

        // ChatHistories query collides with ChatHistories type → ChatHistoriesQuery
        var chatHistDir = Path.Combine(_outputDir, "Trains", "ChatHistories", "ChatHistoriesQuery");
        Directory.Exists(chatHistDir).Should().BeTrue();
        File.Exists(Path.Combine(chatHistDir, "IChatHistoriesQueryTrain.cs")).Should().BeTrue();
    }

    [Test]
    public async Task GenerateTrainsLibrary_TypeCollision_NonCollidingOperationDirsUnchanged()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("type-collision.graphql"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        // GetPlayer doesn't collide
        var getPlayerDir = Path.Combine(_outputDir, "Trains", "Players", "GetPlayer");
        Directory.Exists(getPlayerDir).Should().BeTrue();

        // CreateChat doesn't collide
        var createChatDir = Path.Combine(_outputDir, "Trains", "Chats", "CreateChat");
        Directory.Exists(createChatDir).Should().BeTrue();
    }

    [Test]
    public async Task GenerateTrainsLibrary_TypeCollision_OutputTypeRefUsesGlobalQualification()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("type-collision.graphql"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var interfaceFile = Path.Combine(
            _outputDir,
            "Trains",
            "AllChats",
            "AllChatsQuery",
            "IAllChatsQueryTrain.cs"
        );
        var content = await File.ReadAllTextAsync(interfaceFile).ConfigureAwait(false);

        // Should use global:: qualification to avoid CS0118
        content.Should().Contain("global::TestProject.Trains.Models.AllChats");
    }

    [Test]
    public async Task GenerateTrainsLibrary_TypeCollision_TrainImplUsesGlobalQualification()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("type-collision.graphql"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var trainFile = Path.Combine(
            _outputDir,
            "Trains",
            "AllChats",
            "AllChatsQuery",
            "AllChatsQueryTrain.cs"
        );
        var content = await File.ReadAllTextAsync(trainFile).ConfigureAwait(false);

        content.Should().Contain("global::TestProject.Trains.Models.AllChats");
    }

    [Test]
    public async Task GenerateTrainsLibrary_TypeCollision_JunctionUsesGlobalQualification()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("type-collision.graphql"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var junctionFile = Path.Combine(
            _outputDir,
            "Trains",
            "AllChats",
            "AllChatsQuery",
            "Junctions",
            "AllChatsQueryJunction.cs"
        );
        var content = await File.ReadAllTextAsync(junctionFile).ConfigureAwait(false);

        content.Should().Contain("global::TestProject.Trains.Models.AllChats");
    }

    [Test]
    public async Task GenerateTrainsLibrary_TypeCollision_ManifestNamesUseDisambiguatedNames()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("type-collision.graphql"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var content = await File.ReadAllTextAsync(Path.Combine(_outputDir, "ManifestNames.cs"))
            .ConfigureAwait(false);

        content.Should().Contain("AllChatsQuery");
        content.Should().Contain("ChatHistoriesQuery");
        content.Should().Contain("\"all-chats-query\"");
        content.Should().Contain("\"chat-histories-query\"");
    }

    [Test]
    public async Task GenerateTrainsLibrary_TypeCollision_ModelFilesStillGenerated()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("type-collision.graphql"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var modelsDir = Path.Combine(_outputDir, "Models");
        File.Exists(Path.Combine(modelsDir, "AllChats.cs")).Should().BeTrue();
        File.Exists(Path.Combine(modelsDir, "ChatHistories.cs")).Should().BeTrue();
        File.Exists(Path.Combine(modelsDir, "Chat.cs")).Should().BeTrue();
        File.Exists(Path.Combine(modelsDir, "ChatEntry.cs")).Should().BeTrue();
        File.Exists(Path.Combine(modelsDir, "Player.cs")).Should().BeTrue();
    }

    #endregion

    #region Nullable

    [Test]
    public async Task GenerateTrainsLibrary_NullableGraphql_InputFieldsHaveNullableMarker()
    {
        var schema = await _parser
            .ParseAsync(FixturePath("nullable.graphql"))
            .ConfigureAwait(false);
        await _generator
            .GenerateTrainsLibraryAsync(schema, _outputDir, "TestProject")
            .ConfigureAwait(false);

        var inputFile = Path.Combine(_outputDir, "Trains", "Searchs", "Search", "SearchInput.cs");
        var content = await File.ReadAllTextAsync(inputFile).ConfigureAwait(false);

        content.Should().Contain("?");
    }

    #endregion
}
