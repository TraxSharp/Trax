using System.ComponentModel;

namespace Trax.Scheduler.Services.Sandbox;

/// <summary>How a sandboxed run ended, as the launcher saw it.</summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/add-trax-sandbox#the-wire">SDK reference</seealso>
[EditorBrowsable(EditorBrowsableState.Never)]
public enum SandboxOutcome
{
    /// <summary>The sandbox exited and left a result the launcher accepted. The train may still have failed inside; the result says.</summary>
    Completed,

    /// <summary>The launcher refused the request before starting anything: its shape, profile, limits or signature.</summary>
    Refused,

    /// <summary>The launcher was already running as many sandboxes as its cap allows.</summary>
    Busy,

    /// <summary>The sandbox ran past its timeout and was killed.</summary>
    TimedOut,

    /// <summary>The sandbox went over its memory and was killed.</summary>
    OutOfMemory,

    /// <summary>The sandbox exited without a usable result (a non-zero exit, no result file, or one too large).</summary>
    Exited,

    /// <summary>The sandbox left output the launcher refuses to hand on: a link, a path out of its directory, or more files or bytes than allowed.</summary>
    OutputRefused,

    /// <summary>The launcher could not start or collect the sandbox. Its own log has the detail.</summary>
    Failed,
}
