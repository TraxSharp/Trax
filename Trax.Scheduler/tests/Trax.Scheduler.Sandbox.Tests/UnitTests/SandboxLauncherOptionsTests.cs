using Microsoft.Extensions.Configuration;
using Trax.Scheduler.Sandbox.Launcher.Configuration;
using Trax.Scheduler.Sandbox.Tests.Fakes;

namespace Trax.Scheduler.Sandbox.Tests.UnitTests;

/// <summary>The launcher refuses to start on any configuration that would run a sandbox unsafely: an unpinned image, root, no key.</summary>
[TestFixture]
public class SandboxLauncherOptionsTests
{
    [Test]
    public void Validate_ThePodmanDefaults_Pass() =>
        FluentActions.Invoking(() => LauncherOptions.Podman().Validate()).Should().NotThrow();

    [Test]
    public void Validate_TheKubernetesDefaults_Pass() =>
        FluentActions.Invoking(() => LauncherOptions.Kubernetes().Validate()).Should().NotThrow();

    [Test]
    public void Defaults_AreKataAndPodman()
    {
        var options = new SandboxLauncherOptions();
        options.Runtime.Should().Be(SandboxRuntime.Kata, "gVisor is only ever chosen by naming it");
    }

    private static void Refused(
        Action<SandboxLauncherOptions> change,
        string why,
        SandboxLauncherOptions? options = null
    )
    {
        options ??= LauncherOptions.Podman();
        change(options);
        FluentActions
            .Invoking(options.Validate)
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*{why}*");
    }

    [Test]
    public void Validate_NoKey_IsRefused() => Refused(o => o.SigningKey = null, "SigningKey");

    [Test]
    public void Validate_ShortKey_IsRefused() =>
        Refused(o => o.SigningKey = new byte[31], "SigningKey");

    [Test]
    public void Validate_NoListener_IsRefused() => Refused(o => o.ListenUrl = null, "exactly one");

    [Test]
    public void Validate_TwoListeners_IsRefused() =>
        Refused(o => o.ListenSocketPath = "/run/l.sock", "exactly one");

    [TestCase("/run/launcher")]
    [TestCase("run/launcher.sock")]
    public void Validate_SocketPathNotAbsoluteDotSock_IsRefused(string path) =>
        Refused(
            o =>
            {
                o.ListenUrl = null;
                o.ListenSocketPath = path;
            },
            ".sock"
        );

    [TestCase("http://localhost:7300")]
    [TestCase("https://0.0.0.0:7300")]
    [TestCase("http://0.0.0.0")]
    [TestCase("not a url")]
    public void Validate_ListenUrlNotAnAddressAndPort_IsRefused(string url) =>
        Refused(o => o.ListenUrl = url, "ListenUrl");

    [TestCase("/")]
    [TestCase("/runs/..")]
    [TestCase("runs")]
    public void Validate_RunsPathRootOrRelative_IsRefused(string path) =>
        Refused(o => o.Kubernetes.RunsPath = path, "RunsPath", LauncherOptions.Kubernetes());

    [TestCase(0)]
    [TestCase(91)]
    public void Validate_RunsMinFreePercentOutOfRange_IsRefused(int percent) =>
        Refused(
            o => o.Kubernetes.RunsMinFreePercent = percent,
            "RunsMinFreePercent",
            LauncherOptions.Kubernetes()
        );

    [Test]
    public void Validate_NoProfile_IsRefused() => Refused(o => o.Profiles.Clear(), "no profile");

    [TestCase("localhost/escape:latest")]
    [TestCase("localhost/escape")]
    [TestCase("localhost/escape@sha256:short")]
    [TestCase(
        "localhost/escape@sha512:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
    )]
    public void Validate_UnpinnedImage_IsRefused(string image) =>
        Refused(o => o.Profiles["escape"].Image = image, "name@sha256");

    [TestCase("0:0")]
    [TestCase("0:1000")]
    [TestCase("1000:0")]
    [TestCase("root")]
    [TestCase("1000")]
    public void Validate_RootOrUnnumberedUser_IsRefused(string user) =>
        Refused(o => o.Profiles["escape"].User = user, "non-root");

    [TestCase("Upper")]
    [TestCase("-dash")]
    [TestCase("a_b")]
    public void Validate_BadProfileName_IsRefused(string name) =>
        Refused(
            o => o.Profiles[name] = new SandboxProfile { Image = LauncherOptions.Image },
            "profile name"
        );

    [Test]
    public void Validate_UnpinnedHolder_IsRefused() =>
        Refused(o => o.Podman.HolderImage = "pause:3.10", "HolderImage");

    [Test]
    public void Validate_RelativePodmanSocket_IsRefused() =>
        Refused(o => o.Podman.SocketPath = "podman.sock", "SocketPath");

    [Test]
    public void Validate_KubernetesWithoutNode_IsRefused() =>
        Refused(o => o.Kubernetes.NodeName = "", "NODE_NAME", LauncherOptions.Kubernetes());

    [TestCase("", "OutputStorageClassName is required")]
    [TestCase("local-path", "enforces no size")]
    public void Validate_KubernetesWithoutAClassThatEnforcesCapacity_IsRefused(
        string storageClass,
        string reason
    ) =>
        Refused(
            o => o.Kubernetes.OutputStorageClassName = storageClass,
            reason,
            LauncherOptions.Kubernetes()
        );

    [Test]
    public void Validate_KubernetesWithAnUnpinnedCollector_IsRefused() =>
        Refused(
            o => o.Kubernetes.CollectorImage = "launcher:latest",
            "CollectorImage",
            LauncherOptions.Kubernetes()
        );

    [Test]
    public void Validate_ZeroConcurrency_IsRefused() =>
        Refused(o => o.Caps.MaxConcurrent = 0, "MaxConcurrent");

    [Test]
    public void Validate_TinyPidsCap_IsRefused() => Refused(o => o.Caps.MaxPids = 4, "MaxPids");

    [Test]
    public void Read_FromConfiguration_BindsEverythingAndTheBase64Key()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Trax:SandboxLauncher:SigningKey"] = Convert.ToBase64String(SandboxHosts.Key),
                    ["Trax:SandboxLauncher:ListenSocketPath"] = "/run/trax/launcher.sock",
                    ["Trax:SandboxLauncher:Runtime"] = "GVisor",
                    ["Trax:SandboxLauncher:Profiles:builder:Image"] = LauncherOptions.Image,
                    ["Trax:SandboxLauncher:Profiles:builder:Command:0"] = "dotnet",
                    ["Trax:SandboxLauncher:Profiles:builder:Command:1"] = "/app/Builder.dll",
                    ["Trax:SandboxLauncher:Caps:MaxConcurrent"] = "2",
                    ["Trax:SandboxLauncher:Podman:SocketPath"] =
                        "/run/user/1001/podman/podman.sock",
                    ["Trax:SandboxLauncher:Podman:HolderImage"] = LauncherOptions.Holder,
                }
            )
            .Build();

        var options = SandboxLauncherOptions.Read(configuration);

        options.SigningKey.Should().Equal(SandboxHosts.Key);
        options.Runtime.Should().Be(SandboxRuntime.GVisor);
        options.Profiles["builder"].Command.Should().Equal("dotnet", "/app/Builder.dll");
        options.Caps.MaxConcurrent.Should().Be(2);
        FluentActions.Invoking(options.Validate).Should().NotThrow();
    }

    [Test]
    public void Read_KeyNotBase64_Throws()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Trax:SandboxLauncher:SigningKey"] = "not base64!",
                }
            )
            .Build();

        FluentActions
            .Invoking(() => SandboxLauncherOptions.Read(configuration))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*base64*");
    }

    [Test]
    public void Read_UnknownSetting_Throws()
    {
        // A misspelt setting would otherwise leave its default in place, silently.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Trax:SandboxLauncher:Caps:MaxConcurent"] = "99",
                }
            )
            .Build();

        FluentActions
            .Invoking(() => SandboxLauncherOptions.Read(configuration))
            .Should()
            .Throw<InvalidOperationException>();
    }
}
