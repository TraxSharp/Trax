using System.ComponentModel;

namespace Trax.Scheduler.Services.Sandbox;

/// <summary>What an output archive may hold, checked before any of it is handed on.</summary>
/// <param name="MaxBytes">The most file bytes, summed over every file, uncompressed.</param>
/// <param name="MaxFiles">The most entries, directories included.</param>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-sandbox">SDK reference</seealso>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record SandboxArchiveLimits(long MaxBytes, int MaxFiles)
{
    /// <summary>The longest path an entry may have, in characters.</summary>
    public const int MaxPathLength = 1024;

    /// <summary>The bytes of tar framing allowed per entry on top of its content: its header blocks and padding.</summary>
    public const int OverheadPerEntry = 4096;

    /// <summary>The most raw tar bytes a reader takes before it stops: content, framing and the end blocks.</summary>
    public long MaxRawBytes => MaxBytes + ((long)MaxFiles + 4) * OverheadPerEntry;
}
