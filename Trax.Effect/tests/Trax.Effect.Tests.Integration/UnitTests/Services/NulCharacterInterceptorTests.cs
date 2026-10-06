using AwesomeAssertions;
using Trax.Effect.Data.Postgres.Services.NulCharacterInterceptor;

namespace Trax.Effect.Tests.Integration.UnitTests.Services;

[TestFixture]
public class NulCharacterInterceptorTests
{
    [Test]
    public void Text_without_a_NUL_is_returned_as_the_same_instance()
    {
        var value = "plain";

        NulCharacterInterceptor.ScrubText(value).Should().BeSameAs(value);
        NulCharacterInterceptor.ScrubJson(value).Should().BeSameAs(value);
    }

    [Test]
    public void A_raw_NUL_in_text_becomes_the_replacement_character() =>
        NulCharacterInterceptor.ScrubText("a\0b").Should().Be("a\uFFFDb");

    [Test]
    public void A_NUL_escape_in_JSON_becomes_the_replacement_escape() =>
        NulCharacterInterceptor
            .ScrubJson("""{"v":"bytes:\u0000end"}""")
            .Should()
            .Be("""{"v":"bytes:\ufffdend"}""");

    [Test]
    public void An_escaped_backslash_before_u0000_is_literal_text_and_kept() =>
        NulCharacterInterceptor
            .ScrubJson("""{"path":"C:\\u0000"}""")
            .Should()
            .Be("""{"path":"C:\\u0000"}""");

    [Test]
    public void Other_escapes_are_kept() =>
        NulCharacterInterceptor
            .ScrubJson("""["\n\"\u0041\\\u0000"]""")
            .Should()
            .Be("""["\n\"\u0041\\\ufffd"]""");

    [Test]
    public void A_raw_NUL_in_JSON_without_an_escape_becomes_the_replacement_character() =>
        NulCharacterInterceptor.ScrubJson("[\"a\0b\"]").Should().Be("[\"a\uFFFDb\"]");

    [Test]
    public void An_escape_too_close_to_the_end_to_be_u0000_is_kept() =>
        NulCharacterInterceptor.ScrubJson("""["\u0000","\n"]""").Should().Be("""["\ufffd","\n"]""");
}
