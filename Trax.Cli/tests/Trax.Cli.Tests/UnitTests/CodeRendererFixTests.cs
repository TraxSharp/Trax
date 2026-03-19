using FluentAssertions;
using Trax.Cli.Generator;
using Trax.Cli.Models;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// Tests for CodeRenderer fixes:
/// - Junction template includes "using LanguageExt" when input is Unit (not just output)
/// - Input template includes Models namespace when fields reference enum types
/// </summary>
[TestFixture]
public class CodeRendererFixTests
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

    private static ApiType UnitType =>
        new()
        {
            Name = "Unit",
            Fields = [],
            IsBuiltIn = true,
        };

    #region RenderJunction_EmptyInput

    [Test]
    public void RenderJunction_EmptyInput_NoLanguageExtUsing()
    {
        var op = MakeOperation(
            "ListAll",
            OperationKind.Query,
            input: UnitType,
            httpMethod: "GET",
            httpPath: "/items"
        );

        var result = _renderer.RenderJunction(op, "MyApi");

        // Empty input records don't need LanguageExt — they use the record name directly
        result.Should().NotContain("using LanguageExt;");
    }

    [Test]
    public void RenderJunction_EmptyInput_UsesInputTypeName()
    {
        var op = MakeOperation("ListAll", OperationKind.Query, input: UnitType);

        var result = _renderer.RenderJunction(op, "MyApi");

        result.Should().Contain("Junction<Unit,");
        result.Should().Contain("Run(Unit input)");
    }

    [Test]
    public void RenderJunction_UnitOutput_ContainsLanguageExtUsing()
    {
        var op = MakeOperation(
            "DeletePlayer",
            OperationKind.Mutation,
            output: UnitType,
            httpMethod: "DELETE",
            httpPath: "/players/{id}"
        );

        var result = _renderer.RenderJunction(op, "MyApi");

        result.Should().Contain("using LanguageExt;");
    }

    [Test]
    public void RenderJunction_EmptyInputAndUnitOutput_ContainsLanguageExtUsing()
    {
        var op = MakeOperation(
            "Ping",
            OperationKind.Query,
            input: UnitType,
            output: UnitType,
            httpMethod: "GET",
            httpPath: "/ping"
        );

        var result = _renderer.RenderJunction(op, "MyApi");

        // LanguageExt is included because the output is Unit
        result.Should().Contain("using LanguageExt;");
        result.Should().Contain("Junction<Unit, Unit>");
    }

    [Test]
    public void RenderJunction_NeitherInputNorOutputUnit_NoLanguageExtUsing()
    {
        var op = MakeOperation("GetPlayer", OperationKind.Query);

        var result = _renderer.RenderJunction(op, "MyApi");

        result.Should().NotContain("using LanguageExt;");
    }

    #endregion

    #region RenderTrainInterface_EmptyInput

    [Test]
    public void RenderTrainInterface_EmptyInput_NoLanguageExtUsing()
    {
        var op = MakeOperation("ListAll", OperationKind.Query, input: UnitType);

        var result = _renderer.RenderTrainInterface(op, "MyApi");

        // Empty input records don't need LanguageExt
        result.Should().NotContain("using LanguageExt;");
    }

    [Test]
    public void RenderTrainInterface_UnitOutput_ContainsLanguageExtUsing()
    {
        var op = MakeOperation("DeletePlayer", OperationKind.Mutation, output: UnitType);

        var result = _renderer.RenderTrainInterface(op, "MyApi");

        result.Should().Contain("using LanguageExt;");
    }

    [Test]
    public void RenderTrainInterface_NeitherUnit_NoLanguageExtUsing()
    {
        var op = MakeOperation("GetPlayer", OperationKind.Query);

        var result = _renderer.RenderTrainInterface(op, "MyApi");

        result.Should().NotContain("using LanguageExt;");
    }

    #endregion

    #region RenderInput_ModelsNamespace

    [Test]
    public void RenderInput_WithModelsNamespace_ContainsUsingDirective()
    {
        _renderer.SetModelsNamespace("MyApi.Trains.Models");
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

        var result = _renderer.RenderInput(op, "MyApi");

        result.Should().Contain("using MyApi.Trains.Models;");
    }

    [Test]
    public void RenderInput_WithoutModelsNamespace_NoModelsUsing()
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

        var result = _renderer.RenderInput(op, "MyApi");

        result.Should().NotContain("using MyApi.Trains.Models;");
    }

    #endregion
}
