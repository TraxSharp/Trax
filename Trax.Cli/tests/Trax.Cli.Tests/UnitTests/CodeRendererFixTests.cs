using AwesomeAssertions;
using Trax.Cli.Generator;
using Trax.Cli.Models;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// Tests for CodeRenderer fixes:
/// - Junction template qualifies Unit as global::Trax.Core.Functional.Unit when input is Unit (not just output)
/// - Input template includes Models namespace when fields reference enum types
/// </summary>
[TestFixture]
public class CodeRendererFixTests
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

    private static ApiType UnitType =>
        new()
        {
            Name = "Unit",
            Fields = [],
            IsBuiltIn = true,
        };

    #region RenderJunction_EmptyInput

    [Test]
    public async Task RenderJunction_EmptyInput_NoFunctionalUsing()
    {
        var op = MakeOperation(
            "ListAll",
            OperationKind.Query,
            input: UnitType,
            httpMethod: "GET",
            httpPath: "/items"
        );

        var result = await _renderer.RenderJunctionAsync(op, "MyApi").ConfigureAwait(false);

        // Empty input records don't need the Unit namespace: they use the record name directly
        result.Should().NotContain("using Trax.Core.Functional;");
    }

    [Test]
    public async Task RenderJunction_EmptyInput_UsesInputTypeName()
    {
        var op = MakeOperation("ListAll", OperationKind.Query, input: UnitType);

        var result = await _renderer.RenderJunctionAsync(op, "MyApi").ConfigureAwait(false);

        result.Should().Contain("Junction<Unit,");
        result.Should().Contain("Run(Unit input)");
    }

    [Test]
    public async Task RenderJunction_UnitOutput_QualifiesTraxUnit()
    {
        var op = MakeOperation(
            "DeletePlayer",
            OperationKind.Mutation,
            output: UnitType,
            httpMethod: "DELETE",
            httpPath: "/players/{id}"
        );

        var result = await _renderer.RenderJunctionAsync(op, "MyApi").ConfigureAwait(false);

        result.Should().Contain("global::Trax.Core.Functional.Unit");
    }

    [Test]
    public async Task RenderJunction_EmptyInputAndUnitOutput_QualifiesTraxUnit()
    {
        var op = MakeOperation(
            "Ping",
            OperationKind.Query,
            input: UnitType,
            output: UnitType,
            httpMethod: "GET",
            httpPath: "/ping"
        );

        var result = await _renderer.RenderJunctionAsync(op, "MyApi").ConfigureAwait(false);

        // The input is the operation's own empty record; the output is Trax's Unit
        result.Should().Contain("Junction<Unit, global::Trax.Core.Functional.Unit>");
    }

    [Test]
    public async Task RenderJunction_NeitherInputNorOutputUnit_NoFunctionalUsing()
    {
        var op = MakeOperation("GetPlayer", OperationKind.Query);

        var result = await _renderer.RenderJunctionAsync(op, "MyApi").ConfigureAwait(false);

        result.Should().NotContain("using Trax.Core.Functional;");
    }

    #endregion

    #region RenderTrainInterface_EmptyInput

    [Test]
    public async Task RenderTrainInterface_EmptyInput_NoFunctionalUsing()
    {
        var op = MakeOperation("ListAll", OperationKind.Query, input: UnitType);

        var result = await _renderer.RenderTrainInterfaceAsync(op, "MyApi").ConfigureAwait(false);

        // Empty input records don't need the Unit namespace
        result.Should().NotContain("using Trax.Core.Functional;");
    }

    [Test]
    public async Task RenderTrainInterface_UnitOutput_QualifiesTraxUnit()
    {
        var op = MakeOperation("DeletePlayer", OperationKind.Mutation, output: UnitType);

        var result = await _renderer.RenderTrainInterfaceAsync(op, "MyApi").ConfigureAwait(false);

        result.Should().Contain("global::Trax.Core.Functional.Unit");
    }

    [Test]
    public async Task RenderTrainInterface_NeitherUnit_NoFunctionalUsing()
    {
        var op = MakeOperation("GetPlayer", OperationKind.Query);

        var result = await _renderer.RenderTrainInterfaceAsync(op, "MyApi").ConfigureAwait(false);

        result.Should().NotContain("using Trax.Core.Functional;");
    }

    #endregion

    #region RenderInput_ModelsNamespace

    [Test]
    public async Task RenderInput_WithModelsNamespace_QualifiesModelTypes()
    {
        _renderer.SetModelsNamespace("MyApi.Trains.Models");
        _renderer.SetModelNames(["Status"]);
        var input = new ApiType
        {
            Name = "ListItemsInput",
            Fields =
            [
                new ApiField
                {
                    Name = "Status",
                    TypeName = "Status",
                    IsRequired = false,
                    IsNullable = true,
                },
            ],
        };
        var op = MakeOperation("ListItems", OperationKind.Query, input: input);

        var result = await _renderer.RenderInputAsync(op, "MyApi").ConfigureAwait(false);

        result.Should().Contain("global::MyApi.Trains.Models.Status? Status");
        result.Should().NotContain("using MyApi.Trains.Models;");
    }

    [Test]
    public async Task RenderInput_WithoutModelsNamespace_NoModelsUsing()
    {
        // Do NOT call SetModelsNamespace
        var input = new ApiType
        {
            Name = "ListItemsInput",
            Fields =
            [
                new ApiField
                {
                    Name = "Limit",
                    TypeName = "int",
                    IsRequired = false,
                    IsNullable = true,
                },
            ],
        };
        var op = MakeOperation("ListItems", OperationKind.Query, input: input);

        var result = await _renderer.RenderInputAsync(op, "MyApi").ConfigureAwait(false);

        result.Should().NotContain("using MyApi.Trains.Models;");
    }

    #endregion
}
