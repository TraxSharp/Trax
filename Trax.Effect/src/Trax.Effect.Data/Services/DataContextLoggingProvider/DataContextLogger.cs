using System.Text.RegularExpressions;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Trax.Effect.Models.Log.DTOs;

namespace Trax.Effect.Data.Services.DataContextLoggingProvider;

public class DataContextLogger(
    ChannelWriter<Effect.Models.Log.Log> logChannel,
    string categoryName,
    LogLevel minimumLogLevel,
    HashSet<string> exactBlacklist,
    List<Regex> wildcardBlacklist
) : ILogger
{
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

    public bool IsEnabled(LogLevel logLevel) => logLevel >= minimumLogLevel;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    private bool IsBlacklisted(string category) =>
        exactBlacklist.Contains(category)
        || wildcardBlacklist.Any(regex => regex.IsMatch(category));
}
