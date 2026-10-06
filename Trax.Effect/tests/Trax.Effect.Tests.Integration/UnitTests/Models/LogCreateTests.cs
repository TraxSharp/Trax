using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Trax.Effect.Models.Log;
using Trax.Effect.Models.Log.DTOs;

namespace Trax.Effect.Tests.Integration.UnitTests.Models;

/// <summary>
/// <see cref="Log.Create"/> hands the database only text it can store: no NUL, and no cut that
/// leaves half a surrogate pair.
/// </summary>
[TestFixture]
public class LogCreateTests
{
    private static Log Create(string message, string category = "Category", Exception? ex = null) =>
        Log.Create(
            new CreateLog
            {
                Level = LogLevel.Warning,
                Message = message,
                CategoryName = category,
                EventId = 0,
                Exception = ex,
            }
        );

    [Test]
    public void Create_strips_NUL_from_every_text_field()
    {
        var log = Create("a\0b", "Cat\0egory", new InvalidOperationException("ex\0message"));

        log.Message.Should().Be("ab");
        log.Category.Should().Be("Category");
        log.Exception.Should().Be("exmessage");
    }

    [Test]
    public void Create_backs_off_a_cut_that_would_split_a_surrogate_pair()
    {
        var log = Create(new string('a', 3999) + "\U0001F600" + "tail");

        log.Message.Should().Be(new string('a', 3999));
    }

    [Test]
    public void Create_keeps_a_surrogate_pair_that_ends_exactly_at_the_limit()
    {
        var message = new string('a', 3998) + "\U0001F600";

        Create(message + "tail").Message.Should().Be(message);
    }

    [Test]
    public void Create_leaves_a_short_message_unchanged()
    {
        Create("hello \U0001F600").Message.Should().Be("hello \U0001F600");
    }
}
