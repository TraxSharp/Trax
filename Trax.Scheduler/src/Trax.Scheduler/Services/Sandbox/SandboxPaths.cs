using System.ComponentModel;

namespace Trax.Scheduler.Services.Sandbox;

/// <summary>Where things are inside a sandbox, shared by the launcher that mounts them and the entry point that reads them.</summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-sandbox#inside-the-sandbox">SDK reference</seealso>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SandboxPaths
{
    /// <summary>The root every sandbox path is under.</summary>
    public const string Root = "/sandbox";

    /// <summary>The input directory's name under the root, mounted read-only.</summary>
    public const string Input = "in";

    /// <summary>The output directory's name under the root: the volume the launcher reads after the sandbox exits.</summary>
    public const string Output = "out";

    /// <summary>The directory under the output whose files are handed back to the hub.</summary>
    public const string Files = "files";

    /// <summary>The scratch directory: a size-capped tmpfs, gone when the sandbox exits.</summary>
    public const string Scratch = "/tmp";

    /// <summary>The run request's file name in the input directory.</summary>
    public const string RequestFile = "request.json";

    /// <summary>The canary request's file name in the input directory; present only for the launcher's startup canary.</summary>
    public const string CanaryFile = "canary.json";

    /// <summary>The result's file name in the output directory.</summary>
    public const string ResultFile = "result.json";

    /// <summary>The canary report's file name in the output directory.</summary>
    public const string CanaryReportFile = "canary-report.json";
}
