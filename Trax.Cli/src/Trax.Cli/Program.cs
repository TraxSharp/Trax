using System.Diagnostics.CodeAnalysis;
using Trax.Cli.Commands;

namespace Trax.Cli;

// The unit tests drive the commands directly and never run Main, so it is kept out of coverage
// rather than carrying a permanent 0%.
[ExcludeFromCodeCoverage]
internal static class Program
{
    private static async Task<int> Main(string[] args) =>
        await (await TraxCommandLine.ParseAsync(args).ConfigureAwait(false))
            .InvokeAsync()
            .ConfigureAwait(false);
}
