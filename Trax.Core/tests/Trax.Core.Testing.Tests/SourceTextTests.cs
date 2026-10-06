using Trax.Core.Testing.Infrastructure;

namespace Trax.Core.Testing.Tests;

/// <summary>
/// Stripping comments and strings is what stops every source-scanning guard from matching
/// inside one. When it goes wrong the guards do not fail, they go blind, which is the
/// failure mode with no symptom.
/// </summary>
public class SourceTextTests
{
    private static string Strip(string source) => SourceText.StripCommentsAndStrings(source);

    /// <summary>
    /// The regression that motivated the single-pass rewrite. Stripping <c>//</c> before string
    /// literals ate the rest of any line holding a URL, and the dangling quote then swallowed
    /// everything to the next quote in the file. Across the 928 test files in the eight code
    /// repos, that lost code in 93 of them, up to 94% of a single file, and two of those losses
    /// were hiding a real guard-pattern violation. A second defect, block comments and multi-line
    /// verbatim strings collapsing to one token, dropped newlines in 148 files, 92 of them the
    /// same ones, so every line number reported after one of those was wrong.
    /// </summary>
    [Test]
    public void Strip_UrlInsideAString_DoesNotSwallowTheRestOfTheFile()
    {
        const string source = """
            var url = "ws://localhost/trax/graphql";
            await Task.Delay(5000);
            var other = "fine";
            """;

        var stripped = Strip(source);

        stripped
            .Should()
            .Contain(
                "Task.Delay(5000)",
                "a // inside a string literal is not a comment, and treating it as one hides "
                    + "every line after it from the guards"
            );
        stripped.Should().NotContain("localhost", "the string's content is still blanked");
    }

    [Test]
    public void Strip_PreservesLengthAndLineCount_SoReportedLineNumbersStayTrue()
    {
        const string source = "var a = \"x\"; // note\n/* multi\n   line */\nvar b = 1;\n";

        var stripped = Strip(source);

        stripped.Length.Should().Be(source.Length, "offsets must survive so line numbers do");
        stripped.Split('\n').Length.Should().Be(source.Split('\n').Length);
        stripped.Should().Contain("var b = 1;");
        stripped.Should().NotContain("note").And.NotContain("multi");
    }

    [TestCase("// Task.Delay(1);", TestName = "line comment")]
    [TestCase("/* Task.Delay(1); */", TestName = "block comment")]
    [TestCase("var s = \"Task.Delay(1);\";", TestName = "regular string")]
    [TestCase("var s = @\"Task.Delay(1);\";", TestName = "verbatim string")]
    [TestCase("var s = $@\"Task.Delay(1);\";", TestName = "interpolated verbatim")]
    [TestCase("var s = \"\"\"Task.Delay(1);\"\"\";", TestName = "raw string")]
    public void Strip_HidesAKeywordInEveryCommentOrLiteralForm(string source)
    {
        Strip(source).Should().NotContain("Task.Delay", "a guard must not match inside one");
    }

    [TestCase("Task.Delay(1); // a note", TestName = "code before a line comment")]
    [TestCase("Task.Delay(1); /* a note */", TestName = "code before a block comment")]
    [TestCase("Task.Delay(1); var s = \"text\";", TestName = "code before a string")]
    [TestCase("var c = '\"'; Task.Delay(1);", TestName = "quote inside a char literal")]
    [TestCase("var s = \"a\\\"b\"; Task.Delay(1);", TestName = "escaped quote in a string")]
    [TestCase(
        "var s = @\"a\"\"b\"; Task.Delay(1);",
        TestName = "doubled quote in a verbatim string"
    )]
    public void Strip_KeepsRealCodeVisible(string source)
    {
        Strip(source)
            .Should()
            .Contain("Task.Delay", "blanking a literal must not consume the code around it");
    }

    [Test]
    public void Strip_UnterminatedString_DoesNotRunAway()
    {
        var stripped = Strip("var s = \"never closed\nTask.Delay(1);\n");

        stripped.Split('\n').Length.Should().Be(3, "line structure survives a malformed literal");
    }
}
