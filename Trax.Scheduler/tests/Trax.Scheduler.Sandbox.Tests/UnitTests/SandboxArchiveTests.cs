using System.Formats.Tar;
using System.Text;
using Trax.Scheduler.Sandbox.Tests.Fakes;
using Trax.Scheduler.Services.Sandbox;
using Trax.Testing;

namespace Trax.Scheduler.Sandbox.Tests.UnitTests;

/// <summary>
/// The output a sandbox leaves is read without extracting it, and refused whole when any entry is
/// a link, a device, a path out of the directory, or over the caps.
///
/// <para>Enforces <c>docs/adr/0021-a-sandboxs-output-comes-back-on-a-volume-the-launcher-reads-after-it-exits.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "docs/adr/0021-a-sandboxs-output-comes-back-on-a-volume-the-launcher-reads-after-it-exits.md"
)]
[TestFixture]
public class SandboxArchiveTests
{
    private const string Adr =
        "docs/adr/0021-a-sandboxs-output-comes-back-on-a-volume-the-launcher-reads-after-it-exits.md";

    private static readonly SandboxArchiveLimits Limits = new(MaxBytes: 1024, MaxFiles: 10);

    private static async Task<(IReadOnlyList<SandboxFile> Files, string? Refused)> ReadAsync(
        TarBuilder tar,
        string prefix = "out/",
        SandboxArchiveLimits? limits = null
    )
    {
        var stream = await tar.BuildAsync().ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
            return await SandboxArchive
                .ReadAsync(stream, limits ?? Limits, prefix, CancellationToken.None)
                .ConfigureAwait(false);
    }

    [Test]
    public async Task ReadAsync_PlainFilesUnderThePrefix_AreReadWithThePrefixRemoved()
    {
        var (files, refused) = await ReadAsync(
            new TarBuilder()
                .Directory("out/")
                .File("out/result.json", "{}")
                .Directory("out/files/")
                .File("out/files/a/b.txt", "hello")
        );

        refused.Should().BeNull();
        files.Select(f => f.Path).Should().Equal("result.json", "files/a/b.txt");
        Encoding.UTF8.GetString(files[1].Content.Span).Should().Be("hello");
    }

    [Test]
    public async Task WriteAsync_ThenReadAsync_RoundTrips()
    {
        var bytes = await SandboxArchive.WriteAsync(
            [
                new SandboxFile("a.txt", "1"u8.ToArray()),
                new SandboxFile("d/b.bin", new byte[] { 0, 1, 2 }),
            ],
            CancellationToken.None
        );
        using var stream = new MemoryStream(bytes);

        var (files, refused) = await SandboxArchive.ReadAsync(
            stream,
            Limits,
            "",
            CancellationToken.None
        );

        refused.Should().BeNull();
        files
            .Select(f => (f.Path, f.Content.ToArray()))
            .Should()
            .BeEquivalentTo(
                new[] { ("a.txt", "1"u8.ToArray()), ("d/b.bin", new byte[] { 0, 1, 2 }) }
            );
    }

    [Test]
    public async Task WriteAsync_SameFiles_SameBytes()
    {
        IReadOnlyList<SandboxFile> files = [new("a", "x"u8.ToArray())];

        (await SandboxArchive.WriteAsync(files, CancellationToken.None))
            .Should()
            .Equal(
                await SandboxArchive.WriteAsync(files, CancellationToken.None),
                "the tar depends on nothing but the files"
            );
    }

    [Test]
    public async Task ReadAsync_EmptyArchive_HasNoFiles()
    {
        using var empty = new MemoryStream(
            await SandboxArchive.WriteAsync([], CancellationToken.None)
        );

        var (files, refused) = await SandboxArchive.ReadAsync(
            empty,
            Limits,
            "",
            CancellationToken.None
        );

        refused.Should().BeNull();
        files.Should().BeEmpty();
    }

    [TestCase(TarEntryType.SymbolicLink, "symbolic link")]
    [TestCase(TarEntryType.HardLink, "hard link")]
    [TestCase(TarEntryType.CharacterDevice, "device")]
    [TestCase(TarEntryType.BlockDevice, "device")]
    [TestCase(TarEntryType.Fifo, "named pipe")]
    public async Task ReadAsync_AnEntryThatIsNotAFileOrDirectory_RefusesTheWholeArchive(
        TarEntryType type,
        string kind
    )
    {
        var (files, refused) = await ReadAsync(
            new TarBuilder().File("out/ok.txt").Entry(type, "out/files/bad", link: "/etc/passwd")
        );

        files.Should().BeEmpty("one bad entry refuses all of it ({0})", Adr);
        refused.Should().Contain(kind).And.Contain("files/bad");
    }

    [TestCase("out/../etc/passwd")]
    [TestCase("out/files/../../x")]
    [TestCase("out/./x")]
    [TestCase("out//x")]
    [TestCase("/etc/passwd")]
    [TestCase("out/a\\b")]
    [TestCase("elsewhere/x")]
    [TestCase("outx/y")]
    public async Task ReadAsync_APathOutsideTheOutput_IsRefused(string name)
    {
        var (files, refused) = await ReadAsync(new TarBuilder().File(name));

        files.Should().BeEmpty();
        refused.Should().Contain("not a plain path inside the output");
    }

    [Test]
    public async Task ReadAsync_AControlCharacterInAName_IsRefusedAndNotEchoed()
    {
        var (_, refused) = await ReadAsync(new TarBuilder().File("out/a\nb\u001b[31m"));

        refused.Should().NotBeNull().And.NotContain("\n").And.NotContain("\u001b");
    }

    [Test]
    public async Task ReadAsync_ThePrefixAsAFile_IsRefused()
    {
        var (_, refused) = await ReadAsync(new TarBuilder().File("out"));

        refused.Should().Contain("is not a directory");
    }

    [Test]
    public async Task ReadAsync_MoreEntriesThanTheCap_IsRefused()
    {
        var tar = new TarBuilder();
        for (var i = 0; i < 11; i++)
            tar.File(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"out/{i}"));

        var (_, refused) = await ReadAsync(tar);

        refused.Should().Contain("more than 10 entries");
    }

    [Test]
    public async Task ReadAsync_ExactlyTheCaps_IsRead()
    {
        var tar = new TarBuilder();
        for (var i = 0; i < 10; i++)
            tar.File(
                string.Create(System.Globalization.CultureInfo.InvariantCulture, $"out/{i}"),
                new string('x', 102)
            );

        var (files, refused) = await ReadAsync(tar, limits: new SandboxArchiveLimits(1020, 10));

        refused.Should().BeNull();
        files.Should().HaveCount(10);
    }

    [Test]
    public async Task ReadAsync_MoreBytesThanTheCap_IsRefused()
    {
        var (_, refused) = await ReadAsync(
            new TarBuilder().File("out/a", new string('x', 600)).File("out/b", new string('x', 600))
        );

        refused.Should().Contain("larger than 1024 bytes");
    }

    [Test]
    public async Task ReadAsync_HeadersPastTheRawCap_StopReading()
    {
        // Entry and byte caps count content; a huge extended header carries none, so the raw cap
        // is what stops a reader spending memory on it.
        var entry = new PaxTarEntry(
            TarEntryType.Directory,
            "out/d/",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["comment"] = new string('x', (int)Limits.MaxRawBytes * 2),
            }
        );
        using var buffer = new MemoryStream();
        var writer = new TarWriter(buffer, TarEntryFormat.Pax, leaveOpen: true);
        await using (writer)
            await writer.WriteEntryAsync(entry);
        buffer.Position = 0;

        var (_, refused) = await SandboxArchive.ReadAsync(
            buffer,
            Limits,
            "out/",
            CancellationToken.None
        );

        refused.Should().Contain("larger than");
    }

    [Test]
    public async Task ReadAsync_SamePathTwice_IsRefused()
    {
        var (_, refused) = await ReadAsync(new TarBuilder().File("out/a", "1").File("out/a", "2"));

        refused.Should().Contain("appears twice");
    }

    [Test]
    public async Task ReadAsync_NotATar_IsRefused()
    {
        using var garbage = new MemoryStream(Encoding.UTF8.GetBytes(new string('z', 2048)));

        var (_, refused) = await SandboxArchive.ReadAsync(
            garbage,
            Limits,
            "",
            CancellationToken.None
        );

        refused.Should().NotBeNull();
    }

    [Test]
    public async Task ReadAsync_PathLongerThanTheCap_IsRefused()
    {
        var (_, refused) = await ReadAsync(
            new TarBuilder().File("out/" + new string('a', SandboxArchiveLimits.MaxPathLength + 1))
        );

        refused.Should().Contain("not a plain path");
    }

    [Test]
    public async Task ReadAsync_PrefixWithoutSlash_Throws() =>
        await FluentActions
            .Awaiting(() =>
                SandboxArchive.ReadAsync(new MemoryStream(), Limits, "out", CancellationToken.None)
            )
            .Should()
            .ThrowAsync<ArgumentException>();

    [Test]
    public async Task ReadAsync_NullStream_Throws() =>
        await FluentActions
            .Awaiting(() =>
                SandboxArchive.ReadAsync(Null.Of<Stream>(), Limits, "", CancellationToken.None)
            )
            .Should()
            .ThrowAsync<ArgumentNullException>();
}
