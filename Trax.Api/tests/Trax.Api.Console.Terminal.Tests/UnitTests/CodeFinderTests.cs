using AwesomeAssertions;
using Trax.Api.Console.Terminal.Code;

namespace Trax.Api.Console.Terminal.Tests.UnitTests;

/// <summary>
/// Jump to code finds a type the host names in the checkout by its name alone, a train by its class before its
/// interface, and opens it at its line in the editors that take one.
/// </summary>
[TestFixture]
public class CodeFinderTests
{
    private DirectoryInfo _root = null!;

    [SetUp]
    public void Checkout()
    {
        _root = Directory.CreateTempSubdirectory("trax-console-code-");
        Write(
            "src/Shop/Trains/IShipOrderTrain.cs",
            "namespace Shop.Trains;\npublic interface IShipOrderTrain { }\n"
        );
        Write(
            "src/Shop/Trains/ShipOrderTrain.cs",
            "namespace Shop.Trains;\n\n// The train.\ninternal sealed class ShipOrderTrain : IShipOrderTrain { }\n"
        );
        Write("src/Shop/Junctions/ChargeCard.cs", "namespace Shop;\npublic class ChargeCard { }\n");
        Write("src/Shop/bin/Debug/Copy.cs", "public class ChargeCard { }\n");
    }

    [TearDown]
    public void Remove() => _root.Delete(recursive: true);

    [Test]
    public void A_train_named_by_its_interface_opens_at_its_class()
    {
        var found = CodeFinder.Find(_root.FullName, "Shop.Trains.IShipOrderTrain");

        found.Should().NotBeNull();
        Path.GetFileName(found!.Path).Should().Be("ShipOrderTrain.cs");
        found.Line.Should().Be(4);
    }

    [Test]
    public void A_junction_is_found_by_its_name_and_build_output_is_not_searched()
    {
        var found = CodeFinder.Find(_root.FullName, "ChargeCard");

        found!.Path.Should().EndWith(Path.Combine("Junctions", "ChargeCard.cs"));
    }

    [TestCase("Missing")]
    [TestCase("not a name; rm -rf")]
    public void A_name_with_no_declaration_finds_nothing(string name) =>
        CodeFinder.Find(_root.FullName, name).Should().BeNull();

    [TestCase("nvim", new[] { "+4", "/x/A.cs" })]
    [TestCase("code --wait", new[] { "--wait", "--goto", "/x/A.cs:4" })]
    [TestCase("gedit", new[] { "/x/A.cs" })]
    public void The_editor_gets_the_line_when_it_takes_one(string editor, string[] arguments) =>
        CodeFinder
            .Open(editor, new SourceLocation("/x/A.cs", 4))
            .ArgumentList.Should()
            .Equal(arguments);

    [Test]
    public void The_editor_is_VISUAL_then_EDITOR_then_vi()
    {
        CodeFinder.Editor(n => n == "VISUAL" ? "code" : "nano").Should().Be("code");
        CodeFinder.Editor(n => n == "EDITOR" ? "nano" : null).Should().Be("nano");
        CodeFinder.Editor(_ => null).Should().Be("vi");
    }

    private void Write(string path, string text)
    {
        var full = Path.Combine(_root.FullName, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }
}
