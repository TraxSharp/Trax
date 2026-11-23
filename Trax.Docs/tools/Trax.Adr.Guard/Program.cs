using Microsoft.Extensions.Logging;
using Trax.Adr.Guard;

using var outputs = ToolOutput.Create();
var output = outputs.CreateLogger("adr-guard");

var parsed = Cli.Parse(args);
if (parsed.Error is not null || parsed.Options is null)
{
    output.Fail(parsed.Error ?? "no options were parsed");
    output.Fail("");
    output.Fail(Cli.Usage);
    return 2;
}

var options = parsed.Options;
output.Say($"Checking ADRs in {options.AdrRoot} under {options.RepoRoot}");
output.Say("");

var results = await GuardRunner.RunAsync(options).ConfigureAwait(false);
var report = new StringWriter();
await using (report.ConfigureAwait(false))
{
    var exitCode = GuardRunner.Report(results, report);
    output.SayLines(report.ToString());
    return exitCode;
}
