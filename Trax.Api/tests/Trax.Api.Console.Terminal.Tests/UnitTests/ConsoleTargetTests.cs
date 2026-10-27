using System.Runtime.Versioning;
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
    [Platform(Exclude = "Win")]
    [UnsupportedOSPlatform("windows")]
    public void A_file_other_users_can_read_is_refused()
    {
        var file = new FileInfo(Path.GetTempFileName());
        try
        {
            File.WriteAllText(file.FullName, "sk_live_1\n");
            File.SetUnixFileMode(
                file.FullName,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead
            );

            FluentActions
                .Invoking(() => FromFile(file))
                .Should()
                .Throw<InvalidOperationException>()
                .WithMessage($"*other users can read*chmod 600 {file.FullName}*");

            File.SetUnixFileMode(file.FullName, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            FromFile(file).Credential.Should().Be(new Credential.ApiKey("sk_live_1"));
        }
        finally
        {
            file.Delete();
        }
    }

    [Test]
    public void A_file_larger_than_a_credential_is_refused()
    {
        var file = new FileInfo(Path.GetTempFileName());
        try
        {
            File.WriteAllText(file.FullName, new string('k', ConsoleTarget.MaxFileSize + 1));

            FluentActions
                .Invoking(() => FromFile(file))
                .Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*larger than 64 KiB*");
        }
        finally
        {
            file.Delete();
        }
    }

    [Test]
    [Platform(Include = "Linux")]
    public void A_device_is_refused() =>
        FluentActions
            .Invoking(() => FromFile(new FileInfo("/dev/zero")))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*not a regular file*");

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

    [Test]
    [Platform(Include = "Linux,MacOsX")]
    [UnsupportedOSPlatform("windows")]
    public async Task A_pipe_is_refused_without_waiting_for_a_writer()
    {
        var dir = Directory.CreateTempSubdirectory("trax-console-fifo-");
        try
        {
            var fifo = Path.Combine(dir.FullName, "key");
            mkfifo(fifo, 0b110_000_000).Should().Be(0, "the test needs a pipe");

            // A blocking open of a pipe waits for a writer forever; the bound only turns that into a failure.
            var reading = Task.Run(() =>
                FluentActions.Invoking(() => FromFile(new FileInfo(fifo)))
            );
            var act = await reading.WaitAsync(TimeSpan.FromSeconds(30));

            act.Should().Throw<InvalidOperationException>().WithMessage("*not a regular file*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Test]
    [Platform(Exclude = "Win")]
    [UnsupportedOSPlatform("windows")]
    public void A_link_is_refused_even_to_a_file_only_you_can_read()
    {
        var dir = Directory.CreateTempSubdirectory("trax-console-link-");
        try
        {
            var target = Path.Combine(dir.FullName, "key");
            File.WriteAllText(target, "sk_live_1\n");
            File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var link = Path.Combine(dir.FullName, "link");
            File.CreateSymbolicLink(link, target);

            FluentActions
                .Invoking(() => FromFile(new FileInfo(link)))
                .Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*is a link*");
            FromFile(new FileInfo(target))
                .Credential.Should()
                .Be(new Credential.ApiKey("sk_live_1"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Test]
    public void A_directory_or_a_missing_file_is_refused()
    {
        var dir = Directory.CreateTempSubdirectory("trax-console-dir-");
        try
        {
            foreach (var path in new[] { dir.FullName, Path.Combine(dir.FullName, "missing") })
                FluentActions
                    .Invoking(() => FromFile(new FileInfo(path)))
                    .Should()
                    .Throw<InvalidOperationException>()
                    .WithMessage("*cannot read*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    // Access masks as Windows writes them: Read, ReadAndExecute, Modify, GENERIC_READ, WriteData.
    [TestCase("S-1-1-0", 0x20089u, "Everyone")]
    [TestCase("S-1-5-32-545", 0x200A9u, "Users")]
    [TestCase("S-1-5-11", 0x301BFu, "Authenticated Users")]
    [TestCase("S-1-5-32-545", 0x80000000u, "Users")]
    [TestCase("S-1-5-32-545", 0x2u, "Users")]
    public void On_Windows_a_file_a_group_of_every_account_may_read_or_change_is_refused(
        string sid,
        uint rights,
        string named
    ) =>
        CredentialFile
            .SharedWith([("S-1-5-21-1-2-3-1001", FullControl, false), (sid, rights, false)])
            .Should()
            .Be(named);

    [Test]
    public void On_Windows_a_file_only_its_owner_SYSTEM_and_Administrators_hold_is_accepted() =>
        CredentialFile
            .SharedWith([
                ("S-1-5-21-1-2-3-1001", FullControl, false),
                ("S-1-5-18", FullControl, false),
                ("S-1-5-32-544", FullControl, false),
                // Passed to children only, and the right to see the file's attributes alone: neither reads it.
                ("S-1-5-32-545", 0x20089u, true),
                ("S-1-1-0", 0x80u, false),
            ])
            .Should()
            .BeNull();

    private const uint FullControl = 0x1F01FF;

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern int mkfifo(
        [System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.LPUTF8Str
        )]
            string path,
        uint mode
    );

    private static ConsoleTarget FromFile(FileInfo file) =>
        ConsoleTarget.Resolve(
            "https://ops.example.com",
            ConsoleTarget.DefaultGraphQLPath,
            apiKeyFile: file,
            tokenFile: null,
            sendText: false,
            _ => null
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
