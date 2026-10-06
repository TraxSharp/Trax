using AwesomeAssertions;
using Trax.Effect.Data.Utils;

namespace Trax.Effect.Tests.Integration.UnitTests.Utils;

/// <summary>
/// A caller's search term reaches a LIKE pattern, so its wildcards must arrive as text. A term
/// that kept them would match far more than it says, and a term of only <c>%</c> would match
/// everything.
/// </summary>
[TestFixture]
public class LikePatternTests
{
    [TestCase("timeout", @"%timeout%")]
    [TestCase("TimeOut", @"%timeout%")]
    [TestCase("50%", @"%50\%%")]
    [TestCase("a_b", @"%a\_b%")]
    [TestCase(@"C:\temp", @"%c:\\temp%")]
    [TestCase(@"\%_", @"%\\\%\_%")]
    [TestCase("", "%%")]
    public void Contains_lowers_the_term_and_escapes_its_wildcards(string term, string expected) =>
        LikePattern.Contains(term).Should().Be(expected);

    [Test]
    public void Contains_refuses_a_null_term() =>
        ((Action)(() => LikePattern.Contains(null!))).Should().Throw<ArgumentNullException>();
}
