using Microsoft.Extensions.Logging;
using Trax.Skills.Publish;

const string usage = """
    Usage:
      trax-skills check --skills <dir>
      trax-skills stage --skills <dir> --version <x.y.z> --license <file> --out <dir>
      trax-skills marketplace     (prints .claude-plugin/marketplace.json as main holds it)
    """;

using var outputs = ToolOutput.Create();
var output = outputs.CreateLogger("trax-skills");

if (args is ["marketplace"])
{
    // The manifest ends with a newline, which the logger writes after each line.
    output.Say(Stager.MarketplaceJson().TrimEnd('\n'));
    return 0;
}

if (args.Length == 0 || args[0] is not ("check" or "stage") || args.Length % 2 == 0)
    return Fail(output, "expected a command and pairs of options");

var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (var i = 1; i < args.Length; i += 2)
    options[args[i]] = args[i + 1];

if (!options.TryGetValue("--skills", out var skills))
    return Fail(output, "--skills is required");

IReadOnlyList<SkillProblem> problems;
if (string.Equals(args[0], "check", StringComparison.Ordinal))
    problems = SkillRules.Check(skills);
else if (
    options.TryGetValue("--version", out var version)
    && options.TryGetValue("--license", out var license)
    && options.TryGetValue("--out", out var outDir)
)
    problems = await Stager.StageAsync(skills, version, license, outDir).ConfigureAwait(false);
else
    return Fail(output, "stage needs --version, --license and --out");

foreach (var problem in problems)
    output.Fail(problem.ToString());
if (problems.Count > 0)
    return 1;

output.Say($"{args[0]}: {SkillRules.Folders(skills).Count} skills, no problems");
return 0;

static int Fail(ILogger output, string why)
{
    output.Fail(why);
    output.Fail(usage);
    return 2;
}
