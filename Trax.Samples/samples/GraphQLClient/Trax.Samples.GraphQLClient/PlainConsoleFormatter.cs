using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Trax.Samples.GraphQLClient;

/// <summary>
/// Writes each log entry as its message alone, with no level or category, so what the demo prints
/// reads as plain lines.
/// </summary>
internal sealed class PlainConsoleFormatter() : ConsoleFormatter(FormatterName)
{
    public const string FormatterName = "plain";

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter
    ) => textWriter.WriteLine(logEntry.Formatter(logEntry.State, logEntry.Exception));
}
