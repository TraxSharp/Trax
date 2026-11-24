using Trax.Scheduler.Sandbox.Launcher;
using Trax.Scheduler.Sandbox.Launcher.Backends;

namespace Trax.Scheduler.Sandbox.Tests.UnitTests;

/// <summary>What the launcher removes on its own: only what it could have made.</summary>
[TestFixture]
public class LauncherHousekeepingTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp() => _dir = Directory.CreateTempSubdirectory("trax-sbx-house-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, recursive: true);

    [TestCase("0123456789abcdef0123456789abcdef", true)]
    [TestCase("0123456789ABCDEF0123456789ABCDEF", false)]
    [TestCase("0123456789abcdef0123456789abcde", false)]
    [TestCase("lost+found", false)]
    [TestCase("..", false)]
    public void IsRunDirectory_OnlyARunId(string name, bool run) =>
        KubernetesSandboxBackend.IsRunDirectory(name).Should().Be(run);

    [Test]
    public async Task RemoveStaleSocket_AnEmptyDotSock_IsRemoved()
    {
        var path = Path.Combine(_dir, "launcher.sock");
        await File.WriteAllBytesAsync(path, []);

        TraxSandboxLauncher.RemoveStaleSocket(path);

        File.Exists(path).Should().BeFalse();
    }

    [Test]
    public async Task RemoveStaleSocket_AFileWithContent_IsLeftAlone()
    {
        var path = Path.Combine(_dir, "launcher.sock");
        await File.WriteAllTextAsync(path, "not a socket");

        TraxSandboxLauncher.RemoveStaleSocket(path);

        File.Exists(path).Should().BeTrue("a misconfigured path must not cost the file at it");
    }

    [Test]
    public async Task RemoveStaleSocket_NotDotSock_IsLeftAlone()
    {
        var path = Path.Combine(_dir, "important");
        await File.WriteAllBytesAsync(path, []);

        TraxSandboxLauncher.RemoveStaleSocket(path);

        File.Exists(path).Should().BeTrue();
    }
}
