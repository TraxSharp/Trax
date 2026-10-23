using AwesomeAssertions;
using Trax.Api.Console.Terminal.Connection;

namespace Trax.Api.Console.Terminal.Tests.UnitTests;

/// <summary>
/// Where the console connects and with what: a credential from a file or the environment and never the command line,
/// one at most, and none sent in the clear to another machine.
/// </summary>
[TestFixture]
public class ConsoleTargetTests
{
    [Test]
    public void The_host_names_the_endpoint_and_its_socket()
    {
        var target = Resolve("https://ops.example.com", env: []);

        target.Endpoint.Should().Be(new Uri("https://ops.example.com/trax/graphql"));
        target.Socket.Should().Be(new Uri("wss://ops.example.com/trax/graphql"));
        target.Credential.Should().BeNull();
    }

    [Test]
    public void The_host_may_come_from_the_environment() =>
        Resolve(null, env: new() { [ConsoleTarget.HostVariable] = "https://ops.example.com" })
            .Endpoint.Host.Should()
            .Be("ops.example.com");

    [Test]
    public void An_api_key_from_the_environment_is_its_first_line_trimmed() =>
        Resolve(
            "https://ops.example.com",
            env: new() { [ConsoleTarget.ApiKeyVariable] = " sk_live_1 \n" }
        )
            .Credential.Should()
            .Be(new Credential.ApiKey("sk_live_1"));

    [Test]
    public void A_file_wins_over_the_environment()
    {
        var file = new FileInfo(Path.GetTempFileName());
        try
        {
            File.WriteAllText(file.FullName, "eyJ.token\n");

            var target = ConsoleTarget.Resolve(
                "https://ops.example.com",
                ConsoleTarget.DefaultGraphQLPath,
                apiKeyFile: null,
                tokenFile: file,
                sendText: false,
                n => n == ConsoleTarget.ApiKeyVariable ? "sk_live_1" : null
            );

            target.Credential.Should().Be(new Credential.Bearer("eyJ.token"));
        }
        finally
        {
            file.Delete();
        }
    }

    [Test]
    public void Two_credentials_are_refused() =>
        FluentActions
            .Invoking(() =>
                Resolve(
                    "https://ops.example.com",
                    env: new()
                    {
                        [ConsoleTarget.ApiKeyVariable] = "k",
                        [ConsoleTarget.TokenVariable] = "t",
                    }
                )
            )
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Give one credential*");

    [Test]
    public void A_credential_is_not_sent_in_the_clear_to_another_machine() =>
        FluentActions
            .Invoking(() =>
                Resolve(
                    "http://ops.example.com",
                    env: new() { [ConsoleTarget.ApiKeyVariable] = "k" }
                )
            )
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*plain HTTP to another machine*");

    [TestCase("http://localhost:5000")]
    [TestCase("http://127.0.0.1:5000")]
    public void Plain_HTTP_to_this_machine_is_allowed(string host) =>
        Resolve(host, env: new() { [ConsoleTarget.ApiKeyVariable] = "k" })
            .Socket.Scheme.Should()
            .Be("ws");

    [TestCase("ops.example.com")]
    [TestCase("ftp://ops.example.com")]
    public void A_host_that_is_not_an_http_url_is_refused(string host) =>
        FluentActions
            .Invoking(() => Resolve(host, env: []))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*is not an http or https URL*");

    [TestCase("//evil.example.com/trax/graphql")]
    [TestCase("//user@evil.example.com/x")]
    [TestCase("trax/graphql")]
    public void A_graphql_path_that_leaves_the_host_is_refused(string path) =>
        FluentActions
            .Invoking(() =>
                ConsoleTarget.Resolve(
                    "https://ops.example.com",
                    path,
                    apiKeyFile: null,
                    tokenFile: null,
                    sendText: false,
                    n => n == ConsoleTarget.ApiKeyVariable ? "k" : null
                )
            )
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*is not a path on the host*");

    [Test]
    public void A_graphql_path_on_the_host_keeps_its_port() =>
        ConsoleTarget
            .Resolve(
                "https://ops.example.com:8443",
                "/ops/graphql",
                apiKeyFile: null,
                tokenFile: null,
                sendText: false,
                _ => null
            )
            .Endpoint.Should()
            .Be(new Uri("https://ops.example.com:8443/ops/graphql"));

    [TestCase("https://admin:hunter2@ops.example.com")]
    [TestCase("https://hunter2@ops.example.com")]
    [TestCase("hunter2@ops.example.com")]
    public void A_host_with_a_user_name_or_password_is_refused_without_printing_it(string host) =>
        FluentActions
            .Invoking(() => Resolve(host, env: []))
            .Should()
            .Throw<InvalidOperationException>()
            .Where(e =>
                e.Message.Contains("user name or password") && !e.Message.Contains("hunter2")
            );

    private static ConsoleTarget Resolve(string? host, Dictionary<string, string> env) =>
        ConsoleTarget.Resolve(
            host,
            ConsoleTarget.DefaultGraphQLPath,
            apiKeyFile: null,
            tokenFile: null,
            sendText: false,
            n => env.GetValueOrDefault(n)
        );
}
