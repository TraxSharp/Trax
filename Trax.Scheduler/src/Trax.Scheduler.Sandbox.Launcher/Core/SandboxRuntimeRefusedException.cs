namespace Trax.Scheduler.Sandbox.Launcher.Core;

/// <summary>The startup canary found that sandboxes would not be isolated as configured; the launcher does not start.</summary>
internal sealed class SandboxRuntimeRefusedException(string message, Exception? inner = null)
    : Exception(message, inner);
