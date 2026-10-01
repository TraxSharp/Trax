using FluentAssertions;
using Trax.Dashboard.Utilities;

namespace Trax.Dashboard.Tests.Integration.UnitTests;

/// <summary>
/// A detail page re-renders on every poll tick, and the JSON it shows can be megabytes, so a
/// field is re-indented only when its text changes.
/// </summary>
[TestFixture]
public class JsonDisplayCacheTests
{
    [Test]
    public void The_same_text_on_a_later_render_is_not_formatted_again()
    {
        var cache = new JsonDisplayCache();
        var first = cache.Format("input", """{"a":1}""");

        // A poll tick reads the row again, so the text is a new string with the same content.
        var again = cache.Format("input", new string("""{"a":1}""".ToCharArray()));

        again.Should().BeSameAs(first, "the formatted text is reused, not rebuilt");
    }

    [Test]
    public void Changed_text_is_formatted_again()
    {
        var cache = new JsonDisplayCache();
        cache.Format("output", """{"a":1}""");

        cache.Format("output", """{"a":2}""").Should().Contain("2");
    }

    [Test]
    public void Each_field_is_kept_apart()
    {
        var cache = new JsonDisplayCache();
        cache.Format("input", """{"a":1}""");

        cache.Format("output", """{"b":1}""").Should().Contain("\"b\"");
        cache.Format("input", """{"a":1}""").Should().Contain("\"a\"");
    }
}
