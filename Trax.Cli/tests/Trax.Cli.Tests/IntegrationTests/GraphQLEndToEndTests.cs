using FluentAssertions;
using Trax.Cli.Generator;
using Trax.Cli.Schema.GraphQL;

namespace Trax.Cli.Tests.IntegrationTests;

[TestFixture]
public class GraphQLEndToEndTests
{
    private GraphQLSchemaParser _parser = null!;
    private TraxProjectGenerator _generator = null!;
    private string _outputDir = null!;

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
    public void GenerateTrainsLibrary_SimpleGraphql_AllExpectedFilesExist()
    {
        var schema = _parser.Parse(FixturePath("simple.graphql"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

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
    public void GenerateTrainsLibrary_SimpleGraphql_ManifestNamesContainsOperations()
    {
        var schema = _parser.Parse(FixturePath("simple.graphql"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var content = File.ReadAllText(Path.Combine(_outputDir, "ManifestNames.cs"));

        content.Should().Contain("GetPlayer");
        content.Should().Contain("CreatePlayer");
        content.Should().Contain("\"get-player\"");
        content.Should().Contain("\"create-player\"");
    }

    [Test]
    public void GenerateTrainsLibrary_SimpleGraphql_QueryTrainHasTraxQueryAttribute()
    {
        var schema = _parser.Parse(FixturePath("simple.graphql"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var trainFile = Path.Combine(
            _outputDir,
            "Trains",
            "Players",
            "GetPlayer",
            "GetPlayerTrain.cs"
        );
        var content = File.ReadAllText(trainFile);

        content.Should().Contain("[TraxQuery");
    }

    [Test]
    public void GenerateTrainsLibrary_SimpleGraphql_MutationTrainHasTraxMutationAttribute()
    {
        var schema = _parser.Parse(FixturePath("simple.graphql"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var trainFile = Path.Combine(
            _outputDir,
            "Trains",
            "Players",
            "CreatePlayer",
            "CreatePlayerTrain.cs"
        );
        var content = File.ReadAllText(trainFile);

        content.Should().Contain("[TraxMutation");
    }

    #endregion

    #region NestedTypes

    [Test]
    public void GenerateTrainsLibrary_NestedTypesGraphql_ModelsContainsSharedTypes()
    {
        var schema = _parser.Parse(FixturePath("nested-types.graphql"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var modelsDir = Path.Combine(_outputDir, "Models");
        File.Exists(Path.Combine(modelsDir, "Order.cs")).Should().BeTrue();
        File.Exists(Path.Combine(modelsDir, "OrderItem.cs")).Should().BeTrue();
        File.Exists(Path.Combine(modelsDir, "Customer.cs")).Should().BeTrue();
    }

    #endregion

    #region Enums

    [Test]
    public void GenerateTrainsLibrary_EnumsGraphql_ModelsContainsStatusEnum()
    {
        var schema = _parser.Parse(FixturePath("enums.graphql"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var enumFile = Path.Combine(_outputDir, "Models", "Status.cs");
        File.Exists(enumFile).Should().BeTrue();

        var content = File.ReadAllText(enumFile);
        content.Should().Contain("PENDING");
        content.Should().Contain("ACTIVE");
        content.Should().Contain("COMPLETED");
        content.Should().Contain("CANCELLED");
    }

    #endregion

    #region Nullable

    [Test]
    public void GenerateTrainsLibrary_NullableGraphql_InputFieldsHaveNullableMarker()
    {
        var schema = _parser.Parse(FixturePath("nullable.graphql"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var inputFile = Path.Combine(_outputDir, "Trains", "Searchs", "Search", "SearchInput.cs");
        var content = File.ReadAllText(inputFile);

        content.Should().Contain("?");
    }

    #endregion
}
