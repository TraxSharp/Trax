using FluentAssertions;
using Trax.Cli.Generator;
using Trax.Cli.Models;

namespace Trax.Cli.Tests.UnitTests;

public class CodeRendererTests
{
    private CodeRenderer _renderer = null!;

    [SetUp]
    public void SetUp()
    {
        _renderer = new CodeRenderer();
    }

    private static ApiOperation MakeOperation(
        string name,
        OperationKind kind,
        ApiType? input = null,
        ApiType? output = null,
        string group = "Players",
        string? httpMethod = null,
        string? httpPath = null
    ) =>
        new()
        {
            Name = name,
            Kind = kind,
            Group = group,
            InputType =
                input
                ?? new ApiType
                {
                    Name = $"{name}Input",
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
            OutputType =
                output
                ?? new ApiType
                {
                    Name = $"{name}Output",
                    Fields =
                    [
                        new ApiField
                        {
                            Name = "Result",
                            TypeName = "string",
                            IsRequired = true,
                        },
                    ],
                },
            HttpMethod = httpMethod,
            HttpPath = httpPath,
        };

    #region RenderTrainInterface

    [Test]
    public void RenderTrainInterface_QueryOperation_ContainsIServiceTrain()
    {
        var op = MakeOperation("GetPlayer", OperationKind.Query);

        var result = _renderer.RenderTrainInterface(op, "MyApi");

        result.Should().Contain("IServiceTrain");
        result.Should().NotContain("[TraxQuery]");
        result.Should().Contain("namespace MyApi.Trains.Players.GetPlayer");
    }

    [Test]
    public void RenderTrainInterface_UnitOutput_ContainsLanguageExtUsing()
    {
        var unitOutput = new ApiType
        {
            Name = "Unit",
            Fields = [],
            IsBuiltIn = true,
        };
        var op = MakeOperation("DeletePlayer", OperationKind.Mutation, output: unitOutput);

        var result = _renderer.RenderTrainInterface(op, "MyApi");

        result.Should().Contain("using LanguageExt");
    }

    #endregion

    #region RenderTrainImplementation

    [Test]
    public void RenderTrainImplementation_MutationOperation_ContainsTraxMutationAttribute()
    {
        var op = MakeOperation("CreatePlayer", OperationKind.Mutation);

        var result = _renderer.RenderTrainImplementation(op, "MyApi");

        result.Should().Contain("[TraxMutation");
    }

    [Test]
    public void RenderTrainImplementation_QueryOperation_ContainsTraxQueryAttribute()
    {
        var op = MakeOperation("GetPlayer", OperationKind.Query);

        var result = _renderer.RenderTrainImplementation(op, "MyApi");

        result.Should().Contain("[TraxQuery");
    }

    [Test]
    public void RenderTrainImplementation_DeclaresAnExposurePostureMarker()
    {
        // An exposed train must declare [TraxAuthorize] or [TraxAllowAnonymous] or the
        // scaffolded host fails at startup. The generator emits [TraxAllowAnonymous] so
        // the project runs out of the box; the user swaps it when they add auth.
        var op = MakeOperation("GetPlayer", OperationKind.Query);

        var result = _renderer.RenderTrainImplementation(op, "MyApi");

        result.Should().Contain("[TraxAllowAnonymous]");
    }

    #endregion

    #region RenderInput

    [Test]
    public void RenderInput_RequiredAndOptionalFields_RendersCorrectly()
    {
        var input = new ApiType
        {
            Name = "CreatePlayerInput",
            Fields =
            [
                new ApiField
                {
                    Name = "Name",
                    TypeName = "string",
                    IsRequired = true,
                },
                new ApiField
                {
                    Name = "Nickname",
                    TypeName = "string",
                    IsRequired = false,
                    IsNullable = true,
                },
            ],
        };
        var op = MakeOperation("CreatePlayer", OperationKind.Mutation, input: input);

        var result = _renderer.RenderInput(op, "MyApi");

        result.Should().Contain("required string");
        result.Should().Contain("?");
    }

    #endregion

    #region RenderJunction

    [Test]
    public void RenderJunction_WithHttpInfo_ContainsMethodAndPathInComment()
    {
        var op = MakeOperation(
            "GetPlayer",
            OperationKind.Query,
            httpMethod: "GET",
            httpPath: "/players/{id}"
        );

        var result = _renderer.RenderJunction(op, "MyApi");

        result.Should().Contain("GET");
        result.Should().Contain("/players/{id}");
    }

    #endregion

    #region RenderEnum

    [Test]
    public void RenderEnum_ProducesValidEnumDeclaration()
    {
        var apiEnum = new ApiEnum
        {
            Name = "PlayerStatus",
            Values = ["Active", "Inactive", "Banned"],
        };

        var result = _renderer.RenderEnum(apiEnum, "MyApi");

        result.Should().Contain("public enum");
        result.Should().Contain("Active");
        result.Should().Contain("Inactive");
        result.Should().Contain("Banned");
        result.Should().Contain("namespace MyApi.Trains.Models;");
    }

    #endregion

    #region RenderTrainsCsproj

    [Test]
    public void RenderTrainsCsproj_IsClassLibraryWithTraxReferences()
    {
        var result = _renderer.RenderTrainsCsproj();

        result.Should().Contain("Microsoft.NET.Sdk");
        result.Should().NotContain("Microsoft.NET.Sdk.Web");
        result.Should().Contain("FrameworkReference Include=\"Microsoft.AspNetCore.App\"");
        result.Should().Contain("Trax.Effect");
        result.Should().Contain("Trax.Mediator");
        result.Should().Contain("Trax.Scheduler");
    }

    #endregion

    #region RenderManifestNames

    [Test]
    public void RenderManifestNames_ProducesStaticClassWithConstants()
    {
        var operations = new List<ApiOperation>
        {
            MakeOperation("GetPlayer", OperationKind.Query),
            MakeOperation("CreatePlayer", OperationKind.Mutation),
        };

        var result = _renderer.RenderManifestNames(operations, "MyApi");

        result.Should().Contain("namespace MyApi.Trains;");
        result.Should().Contain("public static class ManifestNames");
        result.Should().Contain("GetPlayer");
        result.Should().Contain("\"get-player\"");
        result.Should().Contain("CreatePlayer");
        result.Should().Contain("\"create-player\"");
    }

    #endregion

    #region RenderGraphQLNamespaces

    [Test]
    public void RenderGraphQLNamespaces_ProducesStaticClassWithConstants()
    {
        var groups = new[] { "Players", "Matches", "Leaderboard" };

        var result = _renderer.RenderGraphQLNamespaces(groups, "MyApi");

        result.Should().Contain("namespace MyApi.Trains;");
        result.Should().Contain("public static class GraphQLNamespaces");
        result.Should().Contain("Players");
        result.Should().Contain("\"players\"");
        result.Should().Contain("Matches");
        result.Should().Contain("\"matches\"");
        result.Should().Contain("Leaderboard");
        result.Should().Contain("\"leaderboard\"");
    }

    [Test]
    public void RenderGraphQLNamespaces_EmptyGroups_ProducesEmptyClass()
    {
        var groups = Array.Empty<string>();

        var result = _renderer.RenderGraphQLNamespaces(groups, "MyApi");

        result.Should().Contain("public static class GraphQLNamespaces");
        result.Should().NotContain("public const string");
    }

    [Test]
    public void RenderGraphQLNamespaces_SingleGroup_ProducesSingleConstant()
    {
        var groups = new[] { "Users" };

        var result = _renderer.RenderGraphQLNamespaces(groups, "MyApi");

        result.Should().Contain("Users");
        result.Should().Contain("\"users\"");
    }

    #endregion

    #region RenderTrainImplementation_Namespace

    [Test]
    public void RenderTrainImplementation_WithGroup_ContainsNamespaceAttribute()
    {
        var op = MakeOperation("LookupPlayer", OperationKind.Query, group: "Players");

        var result = _renderer.RenderTrainImplementation(op, "MyApi");

        result.Should().Contain("Namespace = GraphQLNamespaces.Players");
        result.Should().Contain("using MyApi.Trains;");
    }

    [Test]
    public void RenderTrainImplementation_MutationWithGroup_ContainsNamespaceAttribute()
    {
        var op = MakeOperation("BanPlayer", OperationKind.Mutation, group: "Players");

        var result = _renderer.RenderTrainImplementation(op, "MyApi");

        result.Should().Contain("[TraxMutation(Namespace = GraphQLNamespaces.Players");
    }

    #endregion

    #region RenderTypeRecord

    [Test]
    public void RenderTypeRecord_NamespaceIncludesTrainsModels()
    {
        var type = new ApiType
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
        };

        var result = _renderer.RenderTypeRecord(type, "MyApi", null);

        result.Should().Contain("namespace MyApi.Trains.Models;");
    }

    #endregion
}
