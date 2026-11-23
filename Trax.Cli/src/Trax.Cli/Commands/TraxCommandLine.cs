using System.CommandLine;

namespace Trax.Cli.Commands;

/// <summary>
/// The <c>trax</c> root command and how its command line is parsed.
/// </summary>
/// <remarks>
/// Response files are off. System.CommandLine reads any token starting with <c>@</c> as the name
/// of a file whose contents replace it, and an npm package specifier such as
/// <c>@traxsharp/state-machine</c> starts with one. No <c>trax</c> command takes a response file.
/// </remarks>
internal static class TraxCommandLine
{
    public static async Task<RootCommand> CreateAsync() =>
        new("Trax CLI — generate Trax API projects from schemas, and deploy them")
        {
            GenerateCommand.Create(),
            await MachineCommand.CreateAsync().ConfigureAwait(false),
            ClientCommand.Create(),
            LexiconCommand.Create(),
            DeployCommand.Create(),
        };

    private static readonly ParserConfiguration Configuration = new()
    {
        ResponseFileTokenReplacer = null,
    };

    public static async Task<ParseResult> ParseAsync(string[] args) =>
        (await CreateAsync().ConfigureAwait(false)).Parse(args, Configuration);
}
