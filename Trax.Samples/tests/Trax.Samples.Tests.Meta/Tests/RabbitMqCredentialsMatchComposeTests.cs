namespace Trax.Samples.Tests.Meta.Tests;

/// <summary>
/// Every RabbitMQ connection string a sample or test ships uses the user the repository's
/// docker-compose creates and the user CI's broker creates. A mismatch is silent: the event
/// receiver logs a warning and retries forever, and a failed publish never fails a run, so the
/// cross-process event path just stops working.
///
/// <para>Not ADR-enforcing: it keeps three copies of one credential in step, which is not a
/// choice between alternatives.</para>
/// </summary>
[TestFixture]
public partial class RabbitMqCredentialsMatchComposeTests
{
    private static (string User, string Pass) FromEnv(string text, string userKey, string passKey)
    {
        string Value(string key) =>
            Regex
                .Match(text, key + @"\s*:\s*(?<v>\S+)", RegexOptions.None, TimeSpan.FromSeconds(1))
                .Groups["v"]
                .Value.Trim('"', '\'');
        return (Value(userKey), Value(passKey));
    }

    [Test]
    public async Task Every_rabbitmq_connection_string_uses_the_compose_and_ci_user()
    {
        var compose = await File.ReadAllTextAsync(
            Path.Combine(RepoRoot.Path, "docker-compose.yml")
        );
        var composeUser = FromEnv(compose, "RABBITMQ_DEFAULT_USER", "RABBITMQ_DEFAULT_PASS");

        var offenders = new List<string>();
        // Every workflow that starts a broker, at the root of the repository Trax.Samples lives in.
        var workflows = await BrokerWorkflowsAsync();
        workflows.Should().NotBeEmpty("CI starts a RabbitMQ broker for the samples' tests");
        foreach (var workflow in workflows)
        {
            var ciUser = FromEnv(
                await File.ReadAllTextAsync(workflow).ConfigureAwait(false),
                "RABBITMQ_DEFAULT_USER",
                "RABBITMQ_DEFAULT_PASS"
            );
            if (ciUser != composeUser)
                offenders.Add(
                    $".github/workflows/{Path.GetFileName(workflow)} creates {ciUser.User}, docker-compose.yml creates {composeUser.User}"
                );
        }

        var files = ConfigAndSourceFiles();

        foreach (var file in files)
        {
            foreach (
                Match match in AmqpUri.Matches(
                    await File.ReadAllTextAsync(file).ConfigureAwait(false)
                )
            )
            {
                var used = (match.Groups["user"].Value, match.Groups["pass"].Value);
                if (used != composeUser)
                    offenders.Add($"{RepoRoot.Relative(file)} connects as {used.Item1}");
            }
        }

        offenders
            .Should()
            .BeEmpty(
                $"docker-compose.yml creates RabbitMQ user '{composeUser.User}'; a sample that "
                    + "connects as anyone else is refused, and nothing reports it. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    private static async Task<List<string>> BrokerWorkflowsAsync()
    {
        var workflows = new List<string>();
        foreach (
            var w in Directory.EnumerateFiles(MonorepoRoot.Combine(".github", "workflows"), "*.yml")
        )
        {
            if (
                (await File.ReadAllTextAsync(w).ConfigureAwait(false)).Contains(
                    "RABBITMQ_DEFAULT_USER",
                    StringComparison.Ordinal
                )
            )
                workflows.Add(w);
        }

        return workflows;
    }

    // The samples' appsettings files and the C# sources of the samples and their tests.
    private static IEnumerable<string> ConfigAndSourceFiles() =>
        Directory
            .EnumerateFiles(
                Path.Combine(RepoRoot.Path, "samples"),
                "appsettings*.json",
                SearchOption.AllDirectories
            )
            .Concat(SourceFiles.CSharp("samples", "tests"))
            .Where(f =>
                !f.Contains(
                    $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
                && !f.Contains(
                    $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                    StringComparison.Ordinal
                )
            );

    [GeneratedRegex(
        @"amqp://(?<user>[^:@/""]+):(?<pass>[^@/""]+)@",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex AmqpUri { get; }
}
