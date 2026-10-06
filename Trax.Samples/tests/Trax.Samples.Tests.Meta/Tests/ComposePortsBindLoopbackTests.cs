namespace Trax.Samples.Tests.Meta.Tests;

/// <summary>
/// Every port a compose file in this repository publishes is bound to <c>127.0.0.1</c>. A bare
/// <c>"5432:5432"</c> binds every interface, and the services behind it log in with the
/// passwords written in the same file, so the database and the RabbitMQ admin would answer
/// anyone on the network the developer's machine is on.
///
/// <para>Enforces <c>docs/adr/0005-sample-infrastructure-listens-on-loopback-only.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0005-sample-infrastructure-listens-on-loopback-only.md")]
[TestFixture]
public class ComposePortsBindLoopbackTests
{
    private static IEnumerable<string> ComposeFiles() =>
        Directory
            .EnumerateFiles(RepoRoot.Path, "*.yml", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(RepoRoot.Path, "*.yaml", SearchOption.AllDirectories))
            .Where(IsComposeFile)
            .OrderBy(p => p, StringComparer.Ordinal);

    private static bool IsComposeFile(string path)
    {
        var s = Path.DirectorySeparatorChar;
        if (
            path.Contains($"{s}node_modules{s}", StringComparison.Ordinal)
            || path.Contains($"{s}bin{s}", StringComparison.Ordinal)
            || path.Contains($"{s}obj{s}", StringComparison.Ordinal)
            || path.Contains($"{s}.git{s}", StringComparison.Ordinal)
        )
            return false;
        var name = Path.GetFileName(path);
        return name.StartsWith("docker-compose", StringComparison.Ordinal)
            || name.StartsWith("compose.", StringComparison.Ordinal);
    }

    [Test]
    public void TheRepositoryHasAComposeFile() =>
        ComposeFiles()
            .Should()
            .NotBeEmpty(
                "docker-compose.yml at the repository root provisions the samples' Postgres; a "
                    + "guard that finds no compose file is passing on nothing"
            );

    [TestCaseSource(nameof(ComposeFiles))]
    public void EveryPublishedPort_BindsLoopback(string composePath)
    {
        var offenders = PublishedPorts(File.ReadAllLines(composePath))
            .Where(p => !p.Mapping.StartsWith("127.0.0.1:", StringComparison.Ordinal))
            .Select(p => $"line {p.Line}: \"{p.Mapping}\"")
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                $"{RepoRoot.Relative(composePath)} publishes these ports on every interface. Prefix "
                    + "each with 127.0.0.1: so the service, and the password written beside it, is "
                    + "reachable only from this machine. See "
                    + "docs/adr/0005-sample-infrastructure-listens-on-loopback-only.md:\n"
                    + string.Join("\n", offenders)
            );
    }

    /// <summary>
    /// The short-syntax entries under every <c>ports:</c> key. The long syntax
    /// (<c>- target: 5432</c>) is reported as an offender unless it names <c>host_ip</c>, which
    /// this reader does not parse, so a switch to it fails loudly rather than slipping past.
    /// </summary>
    private static IEnumerable<(int Line, string Mapping)> PublishedPorts(string[] lines)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("ports:", StringComparison.Ordinal))
                continue;

            var keyIndent = lines[i].Length - trimmed.Length;
            for (var j = i + 1; j < lines.Length; j++)
            {
                var entry = lines[j].TrimStart();
                if (entry.Length == 0 || entry.StartsWith('#'))
                    continue;
                var indent = lines[j].Length - entry.Length;
                if (indent <= keyIndent && !entry.StartsWith('-'))
                    break;
                if (indent < keyIndent)
                    break;
                if (!entry.StartsWith('-'))
                    continue;

                var value = entry[1..].Trim().Trim('"', '\'');
                yield return (j + 1, value);
            }
        }
    }
}
