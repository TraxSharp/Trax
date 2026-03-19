using FluentAssertions;
using Trax.Cli.Generator;
using Trax.Cli.Schema.OpenApi;

namespace Trax.Cli.Tests.IntegrationTests;

[TestFixture]
public class OpenApiEndToEndTests
{
    private OpenApiSchemaParser _parser = null!;
    private TraxProjectGenerator _generator = null!;
    private string _outputDir = null!;

    [SetUp]
    public void SetUp()
    {
        _parser = new OpenApiSchemaParser();
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

    #region Petstore

    [Test]
    public void GenerateTrainsLibrary_Petstore_AllExpectedFilesExist()
    {
        var schema = _parser.Parse(FixturePath("petstore.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        // Trains library files
        File.Exists(Path.Combine(_outputDir, "TestProject.Trains.csproj")).Should().BeTrue();
        File.Exists(Path.Combine(_outputDir, "ManifestNames.cs")).Should().BeTrue();

        // ListPets
        var listPetsDir = Path.Combine(_outputDir, "Trains", "Pets", "ListPets");
        File.Exists(Path.Combine(listPetsDir, "IListPetsTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(listPetsDir, "ListPetsTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(listPetsDir, "Junctions", "ListPetsJunction.cs"))
            .Should()
            .BeTrue();

        // CreatePet
        var createPetDir = Path.Combine(_outputDir, "Trains", "Pets", "CreatePet");
        File.Exists(Path.Combine(createPetDir, "ICreatePetTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(createPetDir, "CreatePetTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(createPetDir, "Junctions", "CreatePetJunction.cs"))
            .Should()
            .BeTrue();

        // GetPet (prefix kept — "Pet" collides with Pet model type)
        var getPetDir = Path.Combine(_outputDir, "Trains", "Pets", "GetPet");
        File.Exists(Path.Combine(getPetDir, "IGetPetTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(getPetDir, "GetPetTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(getPetDir, "Junctions", "GetPetJunction.cs")).Should().BeTrue();

        // DeletePet (prefix kept — "Pet" collides with Pet model type)
        var deletePetDir = Path.Combine(_outputDir, "Trains", "Pets", "DeletePet");
        File.Exists(Path.Combine(deletePetDir, "IDeletePetTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(deletePetDir, "DeletePetTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(deletePetDir, "Junctions", "DeletePetJunction.cs"))
            .Should()
            .BeTrue();
    }

    [Test]
    public void GenerateTrainsLibrary_Petstore_ManifestNamesContainsAllOperations()
    {
        var schema = _parser.Parse(FixturePath("petstore.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var content = File.ReadAllText(Path.Combine(_outputDir, "ManifestNames.cs"));

        content.Should().Contain("ListPets");
        content.Should().Contain("CreatePet");
        content.Should().Contain("GetPet");
        content.Should().Contain("DeletePet");
    }

    [Test]
    public void GenerateTrainsLibrary_Petstore_DeleteJunctionContainsHttpComment()
    {
        var schema = _parser.Parse(FixturePath("petstore.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var junctionFile = Path.Combine(
            _outputDir,
            "Trains",
            "Pets",
            "DeletePet",
            "Junctions",
            "DeletePetJunction.cs"
        );
        var content = File.ReadAllText(junctionFile);

        content.Should().Contain("DELETE /pets/{petId}");
    }

    [Test]
    public void GenerateTrainsLibrary_Petstore_PetModelExists()
    {
        var schema = _parser.Parse(FixturePath("petstore.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var petFile = Path.Combine(_outputDir, "Models", "Pet.cs");
        File.Exists(petFile).Should().BeTrue();
    }

    #endregion

    #region GraphQLNamespaces

    [Test]
    public void GenerateTrainsLibrary_Petstore_GraphQLNamespacesFileGenerated()
    {
        var schema = _parser.Parse(FixturePath("petstore.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var namespacesFile = Path.Combine(_outputDir, "GraphQLNamespaces.cs");
        File.Exists(namespacesFile).Should().BeTrue();

        var content = File.ReadAllText(namespacesFile);
        content.Should().Contain("public static class GraphQLNamespaces");
        content.Should().Contain("Pets");
        content.Should().Contain("\"pets\"");
    }

    [Test]
    public void GenerateTrainsLibrary_Petstore_TrainImplementationReferencesNamespace()
    {
        var schema = _parser.Parse(FixturePath("petstore.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var trainFile = Path.Combine(_outputDir, "Trains", "Pets", "ListPets", "ListPetsTrain.cs");
        var content = File.ReadAllText(trainFile);
        content.Should().Contain("Namespace = GraphQLNamespaces.Pets");
    }

    #endregion

    #region ComplexOpenApi

    [Test]
    public void GenerateTrainsLibrary_ComplexOpenApi_AllOperationsGenerated()
    {
        var schema = _parser.Parse(FixturePath("complex-openapi.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        // ListUsers
        var listUsersDir = Path.Combine(_outputDir, "Trains", "Users", "ListUsers");
        File.Exists(Path.Combine(listUsersDir, "IListUsersTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(listUsersDir, "ListUsersTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(listUsersDir, "Junctions", "ListUsersJunction.cs"))
            .Should()
            .BeTrue();

        // CreateUser
        var createUserDir = Path.Combine(_outputDir, "Trains", "Users", "CreateUser");
        File.Exists(Path.Combine(createUserDir, "ICreateUserTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(createUserDir, "CreateUserTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(createUserDir, "Junctions", "CreateUserJunction.cs"))
            .Should()
            .BeTrue();

        // GetUser (prefix kept — "User" collides with User model type)
        var getUserDir = Path.Combine(_outputDir, "Trains", "Users", "GetUser");
        File.Exists(Path.Combine(getUserDir, "IGetUserTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(getUserDir, "GetUserTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(getUserDir, "Junctions", "GetUserJunction.cs")).Should().BeTrue();

        // Health check (synthesized from path only)
        var healthDir = Path.Combine(_outputDir, "Trains", "Health", "Health");
        File.Exists(Path.Combine(healthDir, "IHealthTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(healthDir, "HealthTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(healthDir, "Junctions", "HealthJunction.cs")).Should().BeTrue();
    }

    [Test]
    public void GenerateTrainsLibrary_ComplexOpenApi_UserModelContainsLongId()
    {
        var schema = _parser.Parse(FixturePath("complex-openapi.json"));
        _generator.GenerateTrainsLibrary(schema, _outputDir, "TestProject");

        var userFile = Path.Combine(_outputDir, "Models", "User.cs");
        File.Exists(userFile).Should().BeTrue();

        var content = File.ReadAllText(userFile);
        content.Should().Contain("long");
    }

    #endregion
}
