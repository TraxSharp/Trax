using System.Text.RegularExpressions;
using Trax.Samples.EnergyHub.E2E.Fixtures;

namespace Trax.Samples.EnergyHub.E2E.HubTests;

/// <summary>
/// Every "Try it" curl command in the hub's Program.cs header works as written: the test reads the
/// commands out of the source file, sends each body and key exactly as curl would, and expects an
/// answer with no errors. Edit an example and this test checks the edit.
/// </summary>
[TestFixture]
public partial class DocumentedExamplesTests : HubTestFixture
{
    [Test]
    public async Task Every_try_it_example_in_the_hub_header_succeeds()
    {
        var examples = TryItExamples();
        examples.Should().HaveCountGreaterThanOrEqualTo(3, "the header documents three examples");

        foreach (var (body, apiKey) in examples)
        {
            var result = await GetGraphQLClient().SendRawAsync(body, apiKey);

            result
                .HasErrors.Should()
                .BeFalse(
                    $"the documented example should work as written: {body}\n"
                        + $"error: {result.FirstErrorMessage}"
                );
        }
    }

    private static List<(string Body, string? ApiKey)> TryItExamples()
    {
        var source = File.ReadAllText(HubProgramPath());
        var header = source[..source.IndexOf("\nusing ", StringComparison.Ordinal)];

        // Each example is one curl command continued over comment lines ending in a backslash.
        var commands = new List<string>();
        var current = new List<string>();
        foreach (var raw in header.Split('\n'))
        {
            var line = raw.TrimStart('/', ' ').TrimEnd();
            if (line.StartsWith("curl ", StringComparison.Ordinal))
                current = [line];
            else if (current.Count == 0)
                continue;
            else
                current.Add(line);

            if (!line.EndsWith('\\'))
            {
                commands.Add(string.Join(' ', current.Select(l => l.TrimEnd('\\'))));
                current = [];
            }
        }

        return commands
            .Select(command =>
            {
                var key = ApiKeyHeader().Match(command);
                return (
                    Body: Body().Match(command).Groups["body"].Value,
                    ApiKey: key.Success ? key.Groups["key"].Value : null
                );
            })
            .ToList();
    }

    private static string HubProgramPath()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Trax.Samples.slnx")))
            dir = dir.Parent;

        dir.Should().NotBeNull("the tests run inside the Trax.Samples repository");
        return Path.Combine(
            dir!.FullName,
            "samples",
            "DistributedWorkers",
            "Trax.Samples.EnergyHub.Hub",
            "Program.cs"
        );
    }

    [GeneratedRegex(@"-d '(?<body>[^']+)'")]
    private static partial Regex Body();

    [GeneratedRegex(@"-H ""X-Api-Key: (?<key>[^""]+)""")]
    private static partial Regex ApiKeyHeader();
}
