using System.CommandLine;

namespace Trax.Cli.Tests.Fakes;

/// <summary>What a command wrote to standard output and standard error, and the exit code it returned.</summary>
internal sealed record CommandOutput(int ExitCode, string Stdout, string Stderr)
{
    /// <summary>Runs <paramref name="run"/> with a writer for each stream and keeps what it wrote.</summary>
    public static async Task<CommandOutput> OfAsync(Func<TextWriter, TextWriter, Task<int>> run)
    {
        var stdout = new StringWriter();
        await using (stdout.ConfigureAwait(false))
        {
            var stderr = new StringWriter();
            await using (stderr.ConfigureAwait(false))
            {
                var exitCode = await run(stdout, stderr).ConfigureAwait(false);
                return new CommandOutput(exitCode, stdout.ToString(), stderr.ToString());
            }
        }
    }

    /// <summary>Invokes a parsed command line the way trax does, with a writer for each stream.</summary>
    public static Task<CommandOutput> InvokeAsync(ParseResult parsed) =>
        OfAsync(
            (stdout, stderr) =>
                parsed.InvokeAsync(new InvocationConfiguration { Output = stdout, Error = stderr })
        );
}
