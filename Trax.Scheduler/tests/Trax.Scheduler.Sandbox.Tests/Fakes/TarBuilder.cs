using System.Formats.Tar;
using System.Text;

namespace Trax.Scheduler.Sandbox.Tests.Fakes;

/// <summary>Builds a tar entry by entry, the way a hostile sandbox's volume could look.</summary>
public sealed class TarBuilder
{
    private readonly List<TarEntry> _entries = [];

    public TarBuilder File(string name, string content = "x")
    {
        _entries.Add(
            new PaxTarEntry(TarEntryType.RegularFile, name)
            {
                DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
            }
        );
        return this;
    }

    public TarBuilder Directory(string name)
    {
        _entries.Add(new PaxTarEntry(TarEntryType.Directory, name));
        return this;
    }

    public TarBuilder Entry(TarEntryType type, string name, string? link = null)
    {
        var entry = new PaxTarEntry(type, name);
        if (link is not null && type is TarEntryType.SymbolicLink or TarEntryType.HardLink)
            entry.LinkName = link;
        _entries.Add(entry);
        return this;
    }

    public async Task<MemoryStream> BuildAsync()
    {
        var buffer = new MemoryStream();
        var writer = new TarWriter(buffer, TarEntryFormat.Pax, leaveOpen: true);
        await using (writer.ConfigureAwait(false))
            foreach (var entry in _entries)
                await writer.WriteEntryAsync(entry).ConfigureAwait(false);
        buffer.Position = 0;
        return buffer;
    }
}
