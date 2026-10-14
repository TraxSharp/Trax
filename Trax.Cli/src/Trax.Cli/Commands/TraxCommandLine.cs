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
    public static RootCommand Create() =>
        new("Trax CLI — generate Trax API projects from schemas")
        {
            GenerateCommand.Create(),
            MachineCommand.Create(),
        };

    private static readonly ParserConfiguration Configuration = new()
    {
        ResponseFileTokenReplacer = null,
    };

    public static ParseResult Parse(string[] args) => Create().Parse(args, Configuration);
}
