using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Trax.Effect.Models.Log.DTOs;

namespace Trax.Effect.Data.Services.DataContextLoggingProvider;

/// <summary>
/// The <see cref="ILogger"/> handed out by <see cref="DataContextLoggingProvider"/> for one category.
/// It turns each accepted log call into a <see cref="Effect.Models.Log.Log"/> row and queues it for the
/// provider's background writer. Infrastructure; not intended to be constructed directly.
/// </summary>
/// <param name="logChannel">The provider's bounded queue of pending rows.</param>
/// <param name="categoryName">The logger category, stored on each row.</param>
/// <param name="minimumLogLevel">Calls below this level are ignored.</param>
/// <param name="exactBlacklist">Categories ignored by exact, case-sensitive match.</param>
/// <param name="wildcardBlacklist">Compiled wildcard patterns; a matching category is ignored.</param>
public class DataContextLogger(
    ChannelWriter<Effect.Models.Log.Log> logChannel,
    string categoryName,
    LogLevel minimumLogLevel,
    HashSet<string> exactBlacklist,
    List<Regex> wildcardBlacklist
) : ILogger
{
    /// <summary>
    /// Queues a <see cref="Effect.Models.Log.Log"/> row for the call unless its level is below the
    /// minimum, its category is blacklisted, or its category is
    /// <c>Microsoft.EntityFrameworkCore.Database.Command</c> (always skipped, since writing the row
    /// itself logs that category). Never blocks: when the queue is full the oldest
    /// pending row is dropped.
    /// </summary>
    /// <typeparam name="TState">The state type supplied by the caller.</typeparam>
    /// <param name="logLevel">Stored as the row's level.</param>
    /// <param name="eventId">Its numeric id is stored; the name is not.</param>
    /// <param name="state">Passed to <paramref name="formatter"/>.</param>
    /// <param name="exception">Stored on the row, if any.</param>
    /// <param name="formatter">Produces the stored message.</param>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter
    )
    {
        if (categoryName == "Microsoft.EntityFrameworkCore.Database.Command")
            return;

        if (logLevel < minimumLogLevel || IsBlacklisted(categoryName))
            return;

        var log = Effect.Models.Log.Log.Create(
            new CreateLog
            {
                Level = logLevel,
                Message = formatter(state, exception),
                CategoryName = categoryName,
                EventId = eventId.Id,
                Exception = exception,
            }
        );

        logChannel.TryWrite(log);
    }

    /// <summary>
    /// True when <paramref name="logLevel"/> is at or above the configured minimum. Does not consult
    /// the blacklist, so a blacklisted category still reports enabled and its calls are dropped in
    /// <see cref="Log{TState}"/>.
    /// </summary>
    /// <param name="logLevel">The level to test.</param>
    public bool IsEnabled(LogLevel logLevel) => logLevel >= minimumLogLevel;

    /// <summary>Scopes are not supported; always returns null and scope state is not stored.</summary>
    /// <typeparam name="TState">The scope state type.</typeparam>
    /// <param name="state">Ignored.</param>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    private bool IsBlacklisted(string category) =>
        exactBlacklist.Contains(category)
        || wildcardBlacklist.Any(regex => regex.IsMatch(category));
}
