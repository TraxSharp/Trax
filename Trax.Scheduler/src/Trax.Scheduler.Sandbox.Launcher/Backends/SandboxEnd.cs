namespace Trax.Scheduler.Sandbox.Launcher.Backends;

/// <summary>How a sandbox's process ended, as its runtime reported it.</summary>
internal enum SandboxEnd
{
    /// <summary>It exited by itself.</summary>
    Exited,

    /// <summary>It ran past its timeout and the launcher killed it.</summary>
    TimedOut,

    /// <summary>It went over its memory and the kernel killed it.</summary>
    OutOfMemory,

    /// <summary>It went over its disk and the launcher killed it.</summary>
    DiskExceeded,

    /// <summary>It could not be started or watched.</summary>
    Failed,
}
