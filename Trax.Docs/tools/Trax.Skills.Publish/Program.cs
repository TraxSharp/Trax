using Trax.Skills.Publish;

const string usage = """
    Usage:
      trax-skills check --skills <dir>
      trax-skills stage --skills <dir> --version <x.y.z> --license <file> --out <dir>
      trax-skills marketplace     (prints .claude-plugin/marketplace.json as main holds it)
    """;

if (args is ["marketplace"])
{
    Console.Write(Stager.MarketplaceJson());
    return 0;
}

if (args.Length == 0 || args[0] is not ("check" or "stage") || args.Length % 2 == 0)
    return Fail("expected a command and pairs of options");

var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (var i = 1; i < args.Length; i += 2)
    options[args[i]] = args[i + 1];

if (!options.TryGetValue("--skills", out var skills))
    return Fail("--skills is required");

IReadOnlyList<SkillProblem> problems;
if (args[0] == "check")
    problems = SkillRules.Check(skills);
else if (
    options.TryGetValue("--version", out var version)
    && options.TryGetValue("--license", out var license)
    && options.TryGetValue("--out", out var output)
)
    problems = Stager.Stage(skills, version, license, output);
else
    return Fail("stage needs --version, --license and --out");

foreach (var problem in problems)
    Console.Error.WriteLine(problem);
if (problems.Count > 0)
    return 1;

Console.WriteLine($"{args[0]}: {SkillRules.Folders(skills).Count} skills, no problems");
return 0;

static int Fail(string why)
{
    Console.Error.WriteLine(why);
    Console.Error.WriteLine(usage);
    return 2;
}
