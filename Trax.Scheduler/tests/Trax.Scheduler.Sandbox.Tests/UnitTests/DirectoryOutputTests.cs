using System.Diagnostics;
using Trax.Scheduler.Sandbox.Launcher.Backends;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Tests.UnitTests;

/// <summary>
/// On a cluster the launcher reads the output directory itself: links are never followed or
/// opened, a named pipe is never opened, and the same caps hold.
///
/// <para>Enforces <c>docs/adr/0021-a-sandboxs-output-comes-back-on-a-volume-the-launcher-reads-after-it-exits.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "docs/adr/0021-a-sandboxs-output-comes-back-on-a-volume-the-launcher-reads-after-it-exits.md"
)]
[TestFixture]
public class DirectoryOutputTests
{
    private const string Adr =
        "docs/adr/0021-a-sandboxs-output-comes-back-on-a-volume-the-launcher-reads-after-it-exits.md";

    private static readonly SandboxArchiveLimits Limits = new(1024, 10);
    private string _dir = "";

    [SetUp]
    public void SetUp() => _dir = Directory.CreateTempSubdirectory("trax-sbx-out-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, recursive: true);

    [Test]
    public async Task ReadAsync_FilesAndDirectories_AreRead()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "files", "d"));
        await File.WriteAllTextAsync(Path.Combine(_dir, "result.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(_dir, "files", "d", "a.txt"), "a");

        var (files, refused) = await DirectoryOutput.ReadAsync(
            _dir,
            Limits,
            CancellationToken.None
        );

        refused.Should().BeNull();
        files.Select(f => f.Path).Should().BeEquivalentTo("result.json", "files/d/a.txt");
    }

    [Test]
    public async Task ReadAsync_ASymlinkToAFileOutside_IsRefusedAndNotRead()
    {
        var secret = Path.Combine(
            Path.GetTempPath(),
            "trax-sbx-secret-" + Guid.NewGuid().ToString("N")
        );
        await File.WriteAllTextAsync(secret, "secret");
        try
        {
            File.CreateSymbolicLink(Path.Combine(_dir, "link"), secret);

            var (files, refused) = await DirectoryOutput.ReadAsync(
                _dir,
                Limits,
                CancellationToken.None
            );

            files.Should().BeEmpty();
            refused.Should().Contain("symbolic link", Adr);
        }
        finally
        {
            File.Delete(secret);
        }
    }

    [Test]
    public async Task ReadAsync_ASymlinkToADirectoryOutside_IsNotDescended()
    {
        Directory.CreateSymbolicLink(Path.Combine(_dir, "etc"), "/etc");

        var (_, refused) = await DirectoryOutput.ReadAsync(_dir, Limits, CancellationToken.None);

        refused.Should().Contain("'etc' is a symbolic link");
    }

    [Test]
    public async Task ReadAsync_ANamedPipe_IsRefusedWithoutOpeningIt()
    {
        // Opening a pipe with no writer would block the launcher for good.
        using (
            var mkfifo =
                Process.Start(
                    new ProcessStartInfo("mkfifo", [Path.Combine(_dir, "pipe")])
                    {
                        UseShellExecute = false,
                    }
                ) ?? throw new InvalidOperationException("no mkfifo")
        )
            await mkfifo.WaitForExitAsync();

        var (_, refused) = await DirectoryOutput
            .ReadAsync(_dir, Limits, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(10), TimeProvider.System);

        refused.Should().Contain("named pipe");
    }

    [Test]
    public async Task ReadAsync_MoreEntriesThanTheCap_IsRefused()
    {
        for (var i = 0; i < 11; i++)
            await File.WriteAllTextAsync(
                Path.Combine(_dir, i.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                "x"
            );

        (await DirectoryOutput.ReadAsync(_dir, Limits, CancellationToken.None))
            .Refused.Should()
            .Contain("more than 10 entries");
    }

    [Test]
    public async Task ReadAsync_MoreBytesThanTheCap_IsRefused()
    {
        await File.WriteAllBytesAsync(Path.Combine(_dir, "big"), new byte[Limits.MaxRawBytes + 1]);

        (await DirectoryOutput.ReadAsync(_dir, Limits, CancellationToken.None))
            .Refused.Should()
            .Contain("larger than");
    }

    [Test]
    public async Task Size_CountsFilesAndNotWhatALinkPointsAt()
    {
        await File.WriteAllBytesAsync(Path.Combine(_dir, "a"), new byte[100]);
        File.CreateSymbolicLink(Path.Combine(_dir, "l"), "/etc/passwd");

        DirectoryOutput.Size(_dir, 10).Should().Be(100);
    }

    [Test]
    public async Task Size_PastTheEntryCap_IsMax()
    {
        for (var i = 0; i < 5; i++)
            await File.WriteAllTextAsync(
                Path.Combine(_dir, i.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                "x"
            );

        DirectoryOutput.Size(_dir, 3).Should().Be(long.MaxValue);
    }
}
