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
    public void A_line_replaces_every_control_and_override(string text, string shown) =>
        TerminalText.Line(text).Should().Be(shown);

    [Test]
    public void A_block_keeps_its_line_breaks_and_tabs() =>
        TerminalText.Block("a\n\tb\u001b[2J").Should().Be("a\n\tb\uFFFD[2J");
}
