using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Sandbox.Tests.UnitTests;

/// <summary>A hub's launcher settings are refused at startup unless they reach one launcher, with a key, safely.</summary>
[TestFixture]
public class SandboxClientOptionsTests
{
    private static readonly byte[] Key = new byte[32];

    private static SandboxClientOptions Valid() =>
        new() { LauncherSocketPath = "/run/trax/launcher.sock", SigningKey = Key };

    [Test]
    public void Validate_SocketAndKey_Passes() =>
        FluentActions.Invoking(() => Valid().Validate()).Should().NotThrow();

    [Test]
    public void Validate_HttpsUrlAndKey_Passes() =>
        FluentActions
            .Invoking(() =>
                new SandboxClientOptions
                {
                    LauncherUrl = "https://launcher.trax-sandbox:8443",
                    SigningKey = Key,
                }.Validate()
            )
            .Should()
            .NotThrow();

    [Test]
    public void Validate_HttpToLoopback_Passes() =>
        FluentActions
            .Invoking(() =>
                new SandboxClientOptions
                {
                    LauncherUrl = "http://127.0.0.1:7300",
                    SigningKey = Key,
                }.Validate()
            )
            .Should()
            .NotThrow();

    [Test]
    public void Validate_HttpOffLoopback_IsRefused() =>
        FluentActions
            .Invoking(() =>
                new SandboxClientOptions
                {
                    LauncherUrl = "http://launcher:8080",
                    SigningKey = Key,
                }.Validate()
            )
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*TLS*");

    [Test]
    public void Validate_HttpOffLoopbackWithOptIn_Passes() =>
        FluentActions
            .Invoking(() =>
                new SandboxClientOptions
                {
                    LauncherUrl = "http://launcher:8080",
                    SigningKey = Key,
                    AllowInsecureTransport = true,
                }.Validate()
            )
            .Should()
            .NotThrow();

    [Test]
    public void Validate_NoLauncher_IsRefused() =>
        FluentActions
            .Invoking(() => new SandboxClientOptions { SigningKey = Key }.Validate())
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*exactly one*");

    [Test]
    public void Validate_BothLaunchers_IsRefused()
    {
        var options = Valid();
        options.LauncherUrl = "https://x";
        FluentActions
            .Invoking(() => options.Validate())
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*exactly one*");
    }

    [Test]
    public void Validate_NoKey_IsRefused() =>
        FluentActions
            .Invoking(() => new SandboxClientOptions { LauncherSocketPath = "/x.sock" }.Validate())
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*SigningKey*");

    [Test]
    public void Validate_ShortKey_IsRefused() =>
        FluentActions
            .Invoking(() =>
                new SandboxClientOptions
                {
                    LauncherSocketPath = "/x.sock",
                    SigningKey = new byte[31],
                }.Validate()
            )
            .Should()
            .Throw<InvalidOperationException>();

    [Test]
    public void Validate_RelativeSocket_IsRefused() =>
        FluentActions
            .Invoking(() =>
                new SandboxClientOptions
                {
                    LauncherSocketPath = "x.sock",
                    SigningKey = Key,
                }.Validate()
            )
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*absolute*");

    [Test]
    public void Validate_NotHttpUrl_IsRefused() =>
        FluentActions
            .Invoking(() =>
                new SandboxClientOptions { LauncherUrl = "ftp://x", SigningKey = Key }.Validate()
            )
            .Should()
            .Throw<InvalidOperationException>();

    [Test]
    public void Validate_NonPositiveCaps_AreRefused()
    {
        var options = Valid();
        options.MaxResponseBodyBytes = 0;
        FluentActions
            .Invoking(() => options.Validate())
            .Should()
            .Throw<InvalidOperationException>();

        options = Valid();
        options.Archive = new(0, 1);
        FluentActions
            .Invoking(() => options.Validate())
            .Should()
            .Throw<InvalidOperationException>();

        options = Valid();
        options.ResponseGrace = TimeSpan.Zero;
        FluentActions
            .Invoking(() => options.Validate())
            .Should()
            .Throw<InvalidOperationException>();
    }
}
