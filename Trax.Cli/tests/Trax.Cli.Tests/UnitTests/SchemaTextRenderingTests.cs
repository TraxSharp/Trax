using AwesomeAssertions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Trax.Cli.Generator;
using Trax.Cli.Models;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// Text taken from a schema (descriptions, HTTP paths) is data, and the generated C# has to keep
/// it that way: a description stays on the one comment line it is rendered into, and inside a
/// string literal it is one literal whose value is the description.
///
/// <para>Enforces cli/0002 (<c>docs/adr/0002-generated-code-refuses-a-schema-name-it-cannot-emit.md</c>).</para>
/// </summary>
[TestFixture]
[Property("adr", "docs/adr/0002-generated-code-refuses-a-schema-name-it-cannot-emit.md")]
public class SchemaTextRenderingTests
{
    private const string MultiLine =
        "First line\nsecond line\r\nthird\u2028fourth\u2029fifth\u0085sixth } class Emitted { }";

    private const string Collapsed =
        "First line second line third fourth fifth sixth } class Emitted { }";

    private const string Adr =
        "see cli/0002 (docs/adr/0002-generated-code-refuses-a-schema-name-it-cannot-emit.md)";

    private CodeRenderer _renderer = null!;

    [SetUp]
    public void SetUp() => _renderer = new CodeRenderer();

    [Test]
    public void An_enum_description_renders_on_one_comment_line()
    {
        var result = _renderer.RenderEnum(
            new ApiEnum
            {
                Name = "Color",
                Values = ["Red"],
                Description = MultiLine,
            },
            "MyApi"
        );

        AssertOnOneDocLine(result, Collapsed);
    }

    // Every bidirectional control, which would let a description reorder how the code around it displays.
    private const string BidiControls =
        "\u061C\u200E\u200F\u202A\u202B\u202C\u202D\u202E\u2066\u2067\u2068\u2069";

    [Test]
    public void A_description_carries_no_bidirectional_control_into_the_generated_code()
    {
        var description = "safe \u202E} class Emitted {\u2066 text\u200F end\u061C";
        var op = Operation(description: description, fieldDescription: description);

        foreach (
            var source in new[]
            {
                _renderer.RenderTrainImplementation(op, "MyApi"),
                _renderer.RenderInput(op, "MyApi"),
            }
        )
            source
                .IndexOfAny(BidiControls.ToCharArray())
                .Should()
                .Be(-1, "a format character in a description is replaced ({0}):\n{1}", Adr, source);
    }

    [Test]
    public void Every_format_character_is_a_space()
    {
        foreach (var c in BidiControls + "\u200B\u2060\uFEFF\u00AD")
        {
            GeneratedText
                .SingleLine($"a{c}b")
                .Should()
                .Be("a b", $"U+{(int)c:X4} is a format character");
            GeneratedText.StringLiteralContent($"a{c}b").Should().Be("a b");
            GeneratedText.DocComment($"a{c}b").Should().Be("a b");
        }
    }

    [Test]
    public void An_enum_value_that_is_a_keyword_is_escaped()
    {
        var result = _renderer.RenderEnum(
            new ApiEnum { Name = "Kind", Values = ["Plain", "class"] },
            "MyApi"
        );

        result.Should().Contain("@class,");
    }

    [Test]
    public void A_field_description_renders_on_one_comment_line()
    {
        var op = Operation(fieldDescription: MultiLine);

        AssertOnOneDocLine(_renderer.RenderInput(op, "MyApi"), Collapsed);
    }

    [Test]
    public void A_doc_comment_escapes_xml_markup()
    {
        var op = Operation(fieldDescription: "a </summary> <b> & c");

        _renderer
            .RenderInput(op, "MyApi")
            .Should()
            .Contain("/// a &lt;/summary&gt; &lt;b&gt; &amp; c");
    }

    [TestCase("ends with a backslash \\")]
    [TestCase("pre\\\"); } } namespace Extra { class Emitted { } } //")]
    [TestCase("quote \" and backslash \\ and both \\\"")]
    public void A_train_description_is_one_string_literal_holding_the_description(
        string description
    )
    {
        var op = Operation(description: description);

        var source = _renderer.RenderTrainImplementation(op, "MyApi");
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();

        root.ContainsDiagnostics.Should()
            .BeFalse("the rendered train must parse ({0}):\n{1}", Adr, source);
        var argument = root.DescendantNodes()
            .OfType<AttributeArgumentSyntax>()
            .Single(a => a.NameEquals?.Name.Identifier.Text == "Description");
        argument
            .Expression.Should()
            .BeOfType<LiteralExpressionSyntax>()
            .Which.Token.ValueText.Should()
            .Be(description, Adr);
        root.DescendantNodes().OfType<ClassDeclarationSyntax>().Should().ContainSingle();
    }

    [Test]
    public void A_train_description_on_several_lines_is_collapsed_into_the_literal()
    {
        var source = _renderer.RenderTrainImplementation(
            Operation(description: MultiLine),
            "MyApi"
        );

        var argument = CSharpSyntaxTree
            .ParseText(source)
            .GetRoot()
            .DescendantNodes()
            .OfType<AttributeArgumentSyntax>()
            .Single(a => a.NameEquals?.Name.Identifier.Text == "Description");
        ((LiteralExpressionSyntax)argument.Expression).Token.ValueText.Should().Be(Collapsed);
    }

    [Test]
    public void An_http_path_renders_on_one_comment_line()
    {
        var op = Operation(httpPath: "/players\n} class Emitted { } //");

        var source = _renderer.RenderJunction(op, "MyApi");

        source.Should().Contain("// Original endpoint: GET /players } class Emitted { } //");
        CSharpSyntaxTree
            .ParseText(source)
            .GetRoot()
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Should()
            .ContainSingle();
    }

    private static void AssertOnOneDocLine(string source, string expectedText)
    {
        var lines = source.Split('\n');
        lines.Should().Contain(l => l.Trim() == "/// " + expectedText);
        CSharpSyntaxTree
            .ParseText(source)
            .GetRoot()
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Should()
            .BeEmpty();
    }

    private static ApiOperation Operation(
        string? description = null,
        string? fieldDescription = null,
        string? httpPath = null
    ) =>
        new()
        {
            Name = "GetPlayer",
            Kind = OperationKind.Query,
            Group = "Players",
            Description = description,
            HttpMethod = httpPath is null ? null : "GET",
            HttpPath = httpPath,
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
                        Description = fieldDescription,
                    },
                ],
            },
            OutputType = new ApiType
            {
                Name = "GetPlayerOutput",
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
        };
}
