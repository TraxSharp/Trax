using AwesomeAssertions;
using Trax.Cli.Generator;
using Trax.Cli.Models;

namespace Trax.Cli.Tests.UnitTests;

public class CodeRendererTests
{
    private CodeRenderer _renderer = new();

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
    public async Task RenderTrainInterface_QueryOperation_ContainsIServiceTrain()
    {
        var op = MakeOperation("GetPlayer", OperationKind.Query);

        var result = await _renderer.RenderTrainInterfaceAsync(op, "MyApi").ConfigureAwait(false);

        result.Should().Contain("IServiceTrain");
        result.Should().NotContain("[TraxQuery]");
        result.Should().Contain("namespace MyApi.Trains.Players.GetPlayer");
    }

    [Test]
    public async Task RenderTrainInterface_UnitOutput_QualifiesTraxUnit()
    {
        var unitOutput = new ApiType
        {
            Name = "Unit",
            Fields = [],
            IsBuiltIn = true,
        };
        var op = MakeOperation("DeletePlayer", OperationKind.Mutation, output: unitOutput);

        var result = await _renderer.RenderTrainInterfaceAsync(op, "MyApi").ConfigureAwait(false);

        result.Should().Contain("global::Trax.Core.Functional.Unit");
    }

    #endregion

    #region RenderTrainImplementation

    [Test]
    public async Task RenderTrainImplementation_MutationOperation_ContainsTraxMutationAttribute()
    {
        var op = MakeOperation("CreatePlayer", OperationKind.Mutation);

        var result = await _renderer
            .RenderTrainImplementationAsync(op, "MyApi")
            .ConfigureAwait(false);

        result.Should().Contain("[TraxMutation");
    }

    [Test]
    public async Task RenderTrainImplementation_QueryOperation_ContainsTraxQueryAttribute()
    {
        var op = MakeOperation("GetPlayer", OperationKind.Query);

        var result = await _renderer
            .RenderTrainImplementationAsync(op, "MyApi")
            .ConfigureAwait(false);

        result.Should().Contain("[TraxQuery");
    }

    [Test]
    public async Task RenderTrainImplementation_RequiresTheUserRole()
    {
        // An exposed train must declare [TraxAuthorize] or [TraxAllowAnonymous] or the
        // scaffolded host fails at startup. A generated train requires the User role, as the
        // hub's own trains do, and the hub's Development demo key carries it.
        foreach (var kind in new[] { OperationKind.Query, OperationKind.Mutation })
        {
            var result = await _renderer
                .RenderTrainImplementationAsync(MakeOperation("GetPlayer", kind), "MyApi")
                .ConfigureAwait(false);

            result.Should().Contain("[TraxAuthorize(Roles = \"User\")]");
            result.Should().NotContain("[TraxAllowAnonymous]");
        }
    }

    #endregion

    #region RenderInput

    [Test]
    public async Task RenderInput_RequiredAndOptionalFields_RendersCorrectly()
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

        var result = await _renderer.RenderInputAsync(op, "MyApi").ConfigureAwait(false);

        result.Should().Contain("required string");
        result.Should().Contain("?");
    }

    #endregion

    #region RenderJunction

    [Test]
    public async Task RenderJunction_WithHttpInfo_ContainsMethodAndPathInComment()
    {
        var op = MakeOperation(
            "GetPlayer",
            OperationKind.Query,
            httpMethod: "GET",
            httpPath: "/players/{id}"
        );

        var result = await _renderer.RenderJunctionAsync(op, "MyApi").ConfigureAwait(false);

        result.Should().Contain("GET");
        result.Should().Contain("/players/{id}");
    }

    #endregion

    #region RenderEnum

    [Test]
    public async Task RenderEnum_ProducesValidEnumDeclaration()
    {
        var apiEnum = new ApiEnum
        {
            Name = "PlayerStatus",
            Values = ["Active", "Inactive", "Banned"],
        };

        var result = await _renderer.RenderEnumAsync(apiEnum, "MyApi").ConfigureAwait(false);

        result.Should().Contain("public enum");
        result.Should().Contain("Active");
        result.Should().Contain("Inactive");
        result.Should().Contain("Banned");
        result.Should().Contain("namespace MyApi.Trains.Models;");
    }

    #endregion

    #region RenderTrainsCsproj

    [Test]
    public async Task RenderTrainsCsproj_IsClassLibraryWithTraxReferences()
    {
        var result = await _renderer.RenderTrainsCsprojAsync().ConfigureAwait(false);

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
    public async Task RenderManifestNames_ProducesStaticClassWithConstants()
    {
        var operations = new List<ApiOperation>
        {
            MakeOperation("GetPlayer", OperationKind.Query),
            MakeOperation("CreatePlayer", OperationKind.Mutation),
        };

        var result = await _renderer
            .RenderManifestNamesAsync(operations, "MyApi")
            .ConfigureAwait(false);

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
    public async Task RenderGraphQLNamespaces_ProducesStaticClassWithConstants()
    {
        var groups = new[] { "Players", "Matches", "Leaderboard" };

        var result = await _renderer
            .RenderGraphQLNamespacesAsync(groups, "MyApi")
            .ConfigureAwait(false);

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
    public async Task RenderGraphQLNamespaces_EmptyGroups_ProducesEmptyClass()
    {
        var groups = Array.Empty<string>();

        var result = await _renderer
            .RenderGraphQLNamespacesAsync(groups, "MyApi")
            .ConfigureAwait(false);

        result.Should().Contain("public static class GraphQLNamespaces");
        result.Should().NotContain("public const string");
    }

    [Test]
    public async Task RenderGraphQLNamespaces_SingleGroup_ProducesSingleConstant()
    {
        var groups = new[] { "Users" };

        var result = await _renderer
            .RenderGraphQLNamespacesAsync(groups, "MyApi")
            .ConfigureAwait(false);

        result.Should().Contain("Users");
        result.Should().Contain("\"users\"");
    }

    #endregion

    #region RenderTrainImplementation_Namespace

    [Test]
    public async Task RenderTrainImplementation_WithGroup_ContainsNamespaceAttribute()
    {
        var op = MakeOperation("LookupPlayer", OperationKind.Query, group: "Players");

        var result = await _renderer
            .RenderTrainImplementationAsync(op, "MyApi")
            .ConfigureAwait(false);

        result.Should().Contain("Namespace = GraphQLNamespaces.Players");
        result.Should().Contain("using MyApi.Trains;");
    }

    [Test]
    public async Task RenderTrainImplementation_MutationWithGroup_ContainsNamespaceAttribute()
    {
        var op = MakeOperation("BanPlayer", OperationKind.Mutation, group: "Players");

        var result = await _renderer
            .RenderTrainImplementationAsync(op, "MyApi")
            .ConfigureAwait(false);

        result.Should().Contain("[TraxMutation(Namespace = GraphQLNamespaces.Players");
    }

    #endregion

    #region RenderTypeRecord

    [Test]
    public async Task RenderTypeRecord_NamespaceIncludesTrainsModels()
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

        var result = await _renderer
            .RenderTypeRecordAsync(type, "MyApi", group: null)
            .ConfigureAwait(false);

        result.Should().Contain("namespace MyApi.Trains.Models;");
    }

    #endregion
}
