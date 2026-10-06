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
public class RabbitMqCredentialsMatchComposeTests
{
    private static readonly Regex AmqpUri = new(
        @"amqp://(?<user>[^:@/""]+):(?<pass>[^@/""]+)@",
        RegexOptions.Compiled
    );

    private static (string User, string Pass) FromEnv(string text, string userKey, string passKey)
    {
        string Value(string key) =>
            Regex.Match(text, key + @"\s*:\s*(?<v>\S+)").Groups["v"].Value.Trim('"', '\'');
        return (Value(userKey), Value(passKey));
    }

    [Test]
    public void Every_rabbitmq_connection_string_uses_the_compose_and_ci_user()
    {
        var compose = File.ReadAllText(Path.Combine(RepoRoot.Path, "docker-compose.yml"));
        var composeUser = FromEnv(compose, "RABBITMQ_DEFAULT_USER", "RABBITMQ_DEFAULT_PASS");

        var offenders = new List<string>();
        // Every workflow that starts a broker, at the root of the repository Trax.Samples lives in.
        var workflows = Directory
            .EnumerateFiles(MonorepoRoot.Combine(".github", "workflows"), "*.yml")
            .Where(w =>
                File.ReadAllText(w).Contains("RABBITMQ_DEFAULT_USER", StringComparison.Ordinal)
            )
            .ToList();
        workflows.Should().NotBeEmpty("CI starts a RabbitMQ broker for the samples' tests");
        foreach (var workflow in workflows)
        {
            var ciUser = FromEnv(
                File.ReadAllText(workflow),
                "RABBITMQ_DEFAULT_USER",
                "RABBITMQ_DEFAULT_PASS"
            );
            if (ciUser != composeUser)
                offenders.Add(
                    $".github/workflows/{Path.GetFileName(workflow)} creates {ciUser.User}, docker-compose.yml creates {composeUser.User}"
                );
        }

        var files = Directory
            .EnumerateFiles(
                Path.Combine(RepoRoot.Path, "samples"),
                "appsettings*.json",
                SearchOption.AllDirectories
            )
            .Concat(SourceFiles.CSharp("samples", "tests"))
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
            )
            .Where(f =>
                !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
            );

        foreach (var file in files)
        {
            foreach (Match match in AmqpUri.Matches(File.ReadAllText(file)))
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
}
