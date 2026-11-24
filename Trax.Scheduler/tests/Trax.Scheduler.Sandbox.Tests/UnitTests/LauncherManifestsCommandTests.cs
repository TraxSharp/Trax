using Microsoft.Extensions.Configuration;
using Trax.Scheduler.Sandbox.Launcher;
using Trax.Scheduler.Sandbox.Tests.Fakes;

namespace Trax.Scheduler.Sandbox.Tests.UnitTests;

/// <summary><c>--trax-sandbox-manifests</c>: the launcher writes the cluster's objects from its own configuration, and refuses an unpinned profile.</summary>
[TestFixture]
public class LauncherManifestsCommandTests
{
    private const string LauncherImage =
        "localhost:5001/launcher@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private string _dir = "";

    [SetUp]
    public void SetUp() => _dir = Directory.CreateTempSubdirectory("trax-sbx-render-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, recursive: true);

    private static IConfiguration Configuration(string image, params string[] args) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["Trax:SandboxLauncher:Profiles:escape:Image"] = image,
                }
            )
            .AddCommandLine(args)
            .Build();

    [Test]
    public async Task RenderManifestsAsync_WritesEveryObject()
    {
        var path = Path.Combine(_dir, "sandbox.yaml");

        await TraxSandboxLauncher.RenderManifestsAsync(
            Configuration(
                LauncherOptions.Image,
                "--launcher-image",
                LauncherImage,
                "--hub-namespace",
                "trax-hub"
            ),
            path,
            CancellationToken.None
        );

        var yaml = await File.ReadAllTextAsync(path);
        yaml.Should()
            .Contain("kind: ValidatingAdmissionPolicy")
            .And.Contain(LauncherImage)
            .And.Contain(LauncherOptions.Image)
            .And.Contain("trax-hub");
    }

    [Test]
    public async Task RenderManifestsAsync_UnpinnedProfile_Throws() =>
        await FluentActions
            .Awaiting(() =>
                TraxSandboxLauncher.RenderManifestsAsync(
                    Configuration(
                        "escape:latest",
                        "--launcher-image",
                        LauncherImage,
                        "--hub-namespace",
                        "trax-hub"
                    ),
                    Path.Combine(_dir, "x.yaml"),
                    CancellationToken.None
                )
            )
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*sha256*");

    [Test]
    public async Task RenderManifestsAsync_NoHubNamespace_Throws() =>
        await FluentActions
            .Awaiting(() =>
                TraxSandboxLauncher.RenderManifestsAsync(
                    Configuration(LauncherOptions.Image, "--launcher-image", LauncherImage),
                    Path.Combine(_dir, "x.yaml"),
                    CancellationToken.None
                )
            )
            .Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*hub-namespace*");
}
