using FluentAssertions;
using Trax.Cli.Generator;
using Trax.Cli.Models;

namespace Trax.Cli.Tests.UnitTests;

public class TraxProjectGeneratorTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"trax-cli-test-{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static ApiSchema MakeSchema(
        List<ApiOperation>? operations = null,
        List<ApiEnum>? enums = null,
        List<ApiType>? types = null
    ) =>
        new()
        {
            SourceFile = "test.json",
            SchemaType = "openapi",
            Operations =
                operations
                ??
                [
                    new ApiOperation
                    {
                        Name = "GetPlayer",
                        Kind = OperationKind.Query,
                        Group = "Players",
                        InputType = new ApiType
                        {
                            Name = "GetPlayerInput",
                            Fields =
                            [
                                new ApiField
                                {
                                    Name = "PlayerId",
                                    TypeName = "Guid",
                                    IsRequired = true,
                                },
                            ],
                        },
                        OutputType = new ApiType
                        {
                            Name = "Player",
                            Fields = [],
                            IsBuiltIn = true,
                        },
                        HttpMethod = "GET",
                        HttpPath = "/players/{playerId}",
                    },
                ],
            Enums = enums ?? [],
            Types = types ?? [],
        };

    #region GenerateTrainsLibrary_CreatesExpectedFiles

    [Test]
    public void GenerateTrainsLibrary_SimpleSchema_CreatesExpectedFiles()
    {
        var generator = new TraxProjectGenerator();
        var schema = MakeSchema();

        generator.GenerateTrainsLibrary(schema, _tempDir, "TestProject");

        File.Exists(Path.Combine(_tempDir, "TestProject.Trains.csproj")).Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "ManifestNames.cs")).Should().BeTrue();

        var trainDir = Path.Combine(_tempDir, "Trains", "Players", "GetPlayer");
        File.Exists(Path.Combine(trainDir, "IGetPlayerTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(trainDir, "GetPlayerTrain.cs")).Should().BeTrue();
        File.Exists(Path.Combine(trainDir, "GetPlayerInput.cs")).Should().BeTrue();
        File.Exists(Path.Combine(trainDir, "Junctions", "GetPlayerJunction.cs")).Should().BeTrue();
    }

    #endregion

    #region GenerateTrainsLibrary_ManifestNames

    [Test]
    public void GenerateTrainsLibrary_ManifestNamesContainsOperationConstants()
    {
        var generator = new TraxProjectGenerator();
        var schema = MakeSchema();

        generator.GenerateTrainsLibrary(schema, _tempDir, "TestProject");

        var manifestPath = Path.Combine(_tempDir, "ManifestNames.cs");
        var content = File.ReadAllText(manifestPath);

        content.Should().Contain("namespace TestProject.Trains;");
        content.Should().Contain("public static class ManifestNames");
        content.Should().Contain("GetPlayer");
        content.Should().Contain("get-player");
    }

    #endregion

    #region GenerateTrainsLibrary_FolderGrouping

    [Test]
    public void GenerateTrainsLibrary_OperationsGroupedByGroup_CreatesFolderPerGroup()
    {
        var generator = new TraxProjectGenerator();
        var schema = MakeSchema(
            operations:
            [
                new ApiOperation
                {
                    Name = "GetPlayer",
                    Kind = OperationKind.Query,
                    Group = "Players",
                    InputType = new ApiType
                    {
                        Name = "GetPlayerInput",
                        Fields =
                        [
                            new ApiField
                            {
                                Name = "Id",
                                TypeName = "Guid",
                                IsRequired = true,
                            },
                        ],
                    },
                    OutputType = new ApiType
                    {
                        Name = "Player",
                        Fields = [],
                        IsBuiltIn = true,
                    },
                },
                new ApiOperation
                {
                    Name = "ListItems",
                    Kind = OperationKind.Query,
                    Group = "Items",
                    InputType = new ApiType
                    {
                        Name = "Unit",
                        Fields = [],
                        IsBuiltIn = true,
                    },
                    OutputType = new ApiType
                    {
                        Name = "ListItemsOutput",
                        Fields =
                        [
                            new ApiField
                            {
                                Name = "Items",
                                TypeName = "List<Item>",
                                IsRequired = true,
                            },
                        ],
                    },
                },
            ]
        );

        generator.GenerateTrainsLibrary(schema, _tempDir, "TestProject");

        Directory
            .Exists(Path.Combine(_tempDir, "Trains", "Players", "GetPlayer"))
            .Should()
            .BeTrue();
        Directory.Exists(Path.Combine(_tempDir, "Trains", "Items", "ListItems")).Should().BeTrue();
    }

    #endregion

    #region GenerateTrainsLibrary_Enums

    [Test]
    public void GenerateTrainsLibrary_WithEnum_CreatesEnumFileInModels()
    {
        var generator = new TraxProjectGenerator();
        var schema = MakeSchema(
            enums: [new ApiEnum { Name = "PlayerStatus", Values = ["Active", "Inactive"] }]
        );

        generator.GenerateTrainsLibrary(schema, _tempDir, "TestProject");

        var enumPath = Path.Combine(_tempDir, "Models", "PlayerStatus.cs");
        File.Exists(enumPath).Should().BeTrue();
        var content = File.ReadAllText(enumPath);
        content.Should().Contain("public enum PlayerStatus");
    }

    #endregion

    #region GenerateTrainsLibrary_SharedTypes

    [Test]
    public void GenerateTrainsLibrary_WithSharedType_CreatesTypeFileInModels()
    {
        var generator = new TraxProjectGenerator();
        var schema = MakeSchema(
            types:
            [
                new ApiType
                {
                    Name = "Address",
                    Fields =
                    [
                        new ApiField
                        {
                            Name = "Street",
                            TypeName = "string",
                            IsRequired = true,
                        },
                        new ApiField
                        {
                            Name = "City",
                            TypeName = "string",
                            IsRequired = true,
                        },
                    ],
                },
            ]
        );

        generator.GenerateTrainsLibrary(schema, _tempDir, "TestProject");

        var typePath = Path.Combine(_tempDir, "Models", "Address.cs");
        File.Exists(typePath).Should().BeTrue();
        var content = File.ReadAllText(typePath);
        content.Should().Contain("Address");
    }

    #endregion

    #region GenerateTrainsLibrary_Csproj

    [Test]
    public void GenerateTrainsLibrary_CsprojIsClassLibrary()
    {
        var generator = new TraxProjectGenerator();
        var schema = MakeSchema();

        generator.GenerateTrainsLibrary(schema, _tempDir, "TestProject");

        var csprojPath = Path.Combine(_tempDir, "TestProject.Trains.csproj");
        var content = File.ReadAllText(csprojPath);

        content.Should().Contain("Microsoft.NET.Sdk");
        content.Should().NotContain("Microsoft.NET.Sdk.Web");
        content.Should().Contain("FrameworkReference Include=\"Microsoft.AspNetCore.App\"");
        content.Should().Contain("Trax.Effect");
        content.Should().Contain("Trax.Mediator");
        content.Should().Contain("Trax.Scheduler");
    }

    #endregion

    #region AddProjectReference

    [Test]
    public void AddProjectReference_InsertsProjectReferenceIntoCsproj()
    {
        Directory.CreateDirectory(_tempDir);
        var csprojPath = Path.Combine(_tempDir, "TestProject.Api.csproj");
        File.WriteAllText(
            csprojPath,
            """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <ItemGroup>
                <PackageReference Include="Trax.Effect" Version="1.*" />
              </ItemGroup>
            </Project>
            """
        );

        TraxProjectGenerator.AddProjectReference(_tempDir, "TestProject.Api", "TestProject.Trains");

        var content = File.ReadAllText(csprojPath);
        content.Should().Contain("ProjectReference");
        content.Should().Contain("TestProject.Trains.csproj");
    }

    #endregion

    #region PatchProgramCs

    [Test]
    public void PatchProgramCs_AddTrainsAssemblyAlongsideProgram()
    {
        Directory.CreateDirectory(_tempDir);
        var programPath = Path.Combine(_tempDir, "Program.cs");
        File.WriteAllText(
            programPath,
            """
            using Trax.Mediator.Extensions;

            builder.Services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(Program).Assembly)
            );
            """
        );

        TraxProjectGenerator.PatchProgramCs(_tempDir, "TestProject");

        var content = File.ReadAllText(programPath);
        content
            .Should()
            .Contain("typeof(Program).Assembly, typeof(TestProject.Trains.ManifestNames).Assembly");
        content.Should().Contain("using TestProject.Trains;");
    }

    [Test]
    public void PatchProgramCs_NoProgramCs_DoesNotThrow()
    {
        Directory.CreateDirectory(_tempDir);

        var act = () => TraxProjectGenerator.PatchProgramCs(_tempDir, "TestProject");

        act.Should().NotThrow();
    }

    #endregion

    #region GenerateTrainsLibrary_Namespaces

    [Test]
    public void GenerateTrainsLibrary_ModelNamespaceIncludesTrains()
    {
        var generator = new TraxProjectGenerator();
        var schema = MakeSchema(
            types:
            [
                new ApiType
                {
                    Name = "Player",
                    Fields =
                    [
                        new ApiField
                        {
                            Name = "Name",
                            TypeName = "string",
                            IsRequired = true,
                        },
                    ],
                },
            ]
        );

        generator.GenerateTrainsLibrary(schema, _tempDir, "TestProject");

        var content = File.ReadAllText(Path.Combine(_tempDir, "Models", "Player.cs"));
        content.Should().Contain("namespace TestProject.Trains.Models;");
    }

    [Test]
    public void GenerateTrainsLibrary_EnumNamespaceIncludesTrains()
    {
        var generator = new TraxProjectGenerator();
        var schema = MakeSchema(
            enums: [new ApiEnum { Name = "Status", Values = ["Active", "Inactive"] }]
        );

        generator.GenerateTrainsLibrary(schema, _tempDir, "TestProject");

        var content = File.ReadAllText(Path.Combine(_tempDir, "Models", "Status.cs"));
        content.Should().Contain("namespace TestProject.Trains.Models;");
    }

    #endregion
}
