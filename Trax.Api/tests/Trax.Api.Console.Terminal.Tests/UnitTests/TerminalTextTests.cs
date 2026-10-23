using AwesomeAssertions;
using Trax.Api.Console.Terminal.Lists;

namespace Trax.Api.Console.Terminal.Tests.UnitTests;

/// <summary>Text from the host reaches the terminal with no escape sequence, override or line break it did not mean.</summary>
[TestFixture]
public class TerminalTextTests
{
    [TestCase("\u001b]52;c;ZXZpbA==\u0007", "\uFFFD]52;c;ZXZpbA==\uFFFD")]
    [TestCase("a\u202Eb\u2066c", "a\uFFFDb\uFFFDc")]
    [TestCase("one\ntwo\u2028three", "one\uFFFDtwo\uFFFDthree")]
    [TestCase("plain", "plain")]
    [TestCase("a\u200Eb\u200Fc\u061Cd", "a\uFFFDb\uFFFDc\uFFFDd")]
    [TestCase(
        "\u202A\u202B\u202C\u202D\u2067\u2068\u2069",
        "\uFFFD\uFFFD\uFFFD\uFFFD\uFFFD\uFFFD\uFFFD"
    )]
    [TestCase("zero\u200Bwidth\uFEFF", "zero\uFFFDwidth\uFFFD")]
    [TestCase("tag\U000E0041", "tag\uFFFD")]
    [TestCase("caf\u00E9 \u2603", "caf\u00E9 \u2603")]
    public void A_line_replaces_every_control_and_override(string text, string shown) =>
        TerminalText.Line(text).Should().Be(shown);

    [Test]
    public void A_lone_surrogate_is_replaced() =>
        TerminalText.Line("lone" + (char)0xD800).Should().Be("lone\uFFFD");

    [Test]
    public void Every_bidirectional_control_is_replaced_in_a_block_too()
    {
        foreach (
            var c in "\u061C\u200E\u200F\u202A\u202B\u202C\u202D\u202E\u2066\u2067\u2068\u2069"
        )
            TerminalText.Block($"a{c}b").Should().Be("a\uFFFDb", $"U+{(int)c:X4} reorders text");
    }

    [Test]
    public void A_block_keeps_its_line_breaks_and_tabs() =>
        TerminalText.Block("a\n\tb\u001b[2J").Should().Be("a\n\tb\uFFFD[2J");
}
