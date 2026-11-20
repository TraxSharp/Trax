using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Trax.Skills.Publish;

/// <summary>
/// Where the tool prints: a console logger that writes each line as it is, and errors to standard
/// error. The factory flushes what is queued when it is disposed.
/// </summary>
internal static class ToolOutput
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

    /// <summary>Prints text line by line, each line as <see cref="Say"/> would.</summary>
    public static void SayLines(this ILogger output, string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var count = lines.Length > 0 && lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
        for (var i = 0; i < count; i++)
            output.Say(lines[i]);
    }
}
