using Trax.Adr.Guard;

var parsed = Cli.Parse(args);
if (parsed.Error is not null)
{
    Console.Error.WriteLine(parsed.Error);
    Console.Error.WriteLine();
    Console.Error.WriteLine(Cli.Usage);
    return 2;
}

var options = parsed.Options!;
Console.WriteLine($"Checking ADRs in {options.AdrRoot} under {options.RepoRoot}");
Console.WriteLine();

return GuardRunner.Report(GuardRunner.Run(options), Console.Out);
