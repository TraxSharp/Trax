using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Trax.Samples.PersistedOperations.Client;

/// <summary>
/// Where the demo prints: a console logger that writes each line as it is, and errors to standard
/// error. The factory flushes what is queued when it is disposed.
/// </summary>
internal static class DemoOutput
{
    public static ILoggerFactory Create() =>
        LoggerFactory.Create(logging =>
            logging
                .AddConsole(console =>
                {
                    console.FormatterName = PlainConsoleFormatter.FormatterName;
                    console.LogToStandardErrorThreshold = LogLevel.Error;
                })
                .AddConsoleFormatter<PlainConsoleFormatter, ConsoleFormatterOptions>()
        );

    /// <summary>Prints one line.</summary>
    public static void Say(this ILogger output, string line) =>
        output.LogInformation("{Line}", line);

    /// <summary>Prints one line to standard error.</summary>
    public static void Fail(this ILogger output, string line) => output.LogError("{Line}", line);
}
