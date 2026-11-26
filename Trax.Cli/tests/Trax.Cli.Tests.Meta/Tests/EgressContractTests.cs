using System.Text.RegularExpressions;
using AwesomeAssertions;
using Trax.Cli.Tests.Meta.Infrastructure;

namespace Trax.Cli.Tests.Meta.Tests;

/// <summary>
/// The egress image and the trax tool that renders it are built apart (Go in <c>Trax.Egress</c>, C# here) but must
/// agree on the numbers between them: the uid the rules let out, the loopback ports of the resolver and the proxy,
/// the commands, and the variables the list is read from. A drift on either side would deploy a sidecar the rules
/// do not let out, or one the app's traffic is not sent to, and both fail only on a real cluster. This reads the
/// constants from both sources and fails on any difference.
///
/// <para>Enforces <c>Trax.Docs/adr/0064-egress-is-one-image-released-with-trax.md</c>.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0064-egress-is-one-image-released-with-trax.md")]
[TestFixture]
public class EgressContractTests
{
    private const string Because =
        "the trax tool renders the egress image with these numbers, and Trax.Docs/adr/0064 holds the two to one "
        + "contract. Change both sides together";

    private static Task<string> Go() =>
        File.ReadAllTextAsync(MonorepoRoot.Combine("Trax.Egress", "cmd", "trax-egress", "main.go"));

    private static Task<string> CSharp() =>
        File.ReadAllTextAsync(RepoRoot.Combine("src", "Trax.Cli", "Deploy", "DeployEgress.cs"));

    private static string GoConstant(string source, string name)
    {
        var m = Regex.Match(
            source,
            $@"^\s*{name}\s*=\s*(\S+)\s*$",
            RegexOptions.Multiline,
            TimeSpan.FromSeconds(1)
        );
        m.Success.Should().BeTrue($"main.go declares {name}, because {Because}");
        return m.Groups[1].Value.Trim('"');
    }

    private static string CSharpConstant(string source, string name)
    {
        var m = Regex.Match(
            source,
            $@"public const (?:int|string) {name} = ""?([^"";]+)""?;",
            RegexOptions.None,
            TimeSpan.FromSeconds(1)
        );
        m.Success.Should().BeTrue($"DeployEgress declares {name}, because {Because}");
        return m.Groups[1].Value;
    }

    [TestCase("UID", "Uid")]
    [TestCase("DNSPort", "DnsPort")]
    [TestCase("ProxyPort", "ProxyPort")]
    public async Task The_tool_and_the_image_agree_on_the_uid_and_ports(string go, string csharp)
    {
        var expected = GoConstant(await Go().ConfigureAwait(false), go);
        CSharpConstant(await CSharp().ConfigureAwait(false), csharp)
            .Should()
            .Be(expected, $"{go} in main.go is {csharp} in DeployEgress, because {Because}");
    }

    [TestCase("RulesCommand", "rules")]
    [TestCase("ServeCommand", "serve")]
    [TestCase("RunCommand", "run")]
    [TestCase("ReadyCommand", "ready")]
    public async Task Every_command_the_tool_renders_is_one_the_image_has(
        string csharp,
        string command
    )
    {
        CSharpConstant(await CSharp().ConfigureAwait(false), csharp).Should().Be(command);
        (await Go().ConfigureAwait(false))
            .Should()
            .Contain(
                $"case \"{command}\":",
                $"the image must answer '{command}', because {Because}"
            );
    }

    [TestCase("AllowVariable", "EnvAllow")]
    [TestCase("PostgresVariable", "EnvPostgres")]
    public async Task The_list_is_read_from_the_variables_the_tool_sets(string csharp, string go)
    {
        var allow = await File.ReadAllTextAsync(
                MonorepoRoot.Combine("Trax.Egress", "internal", "allow", "allow.go")
            )
            .ConfigureAwait(false);
        CSharpConstant(await CSharp().ConfigureAwait(false), csharp)
            .Should()
            .Be(
                GoConstant(allow, go),
                $"{go} in allow.go is {csharp} in DeployEgress, because {Because}"
            );
    }

    [Test]
    public async Task The_image_runs_as_the_egress_uid_by_default()
    {
        var dockerfile = await File.ReadAllTextAsync(
                MonorepoRoot.Combine("Trax.Egress", "Dockerfile")
            )
            .ConfigureAwait(false);
        var uid = CSharpConstant(await CSharp().ConfigureAwait(false), "Uid");
        dockerfile
            .Should()
            .Contain($"USER {uid}:{uid}", $"serve runs as the egress uid, because {Because}");
        dockerfile
            .Should()
            .Contain(
                "/usr/local/bin/trax-egress",
                $"the startup probe and the healthcheck call it there, because {Because}"
            );
    }
}
