using System.CommandLine;
using Trax.Cli.Commands;

var rootCommand = new RootCommand("Trax CLI — generate Trax API projects from schemas")
{
    GenerateCommand.Create(),
    MachineCommand.Create(),
};

var parseResult = rootCommand.Parse(args);
return await parseResult.InvokeAsync();
