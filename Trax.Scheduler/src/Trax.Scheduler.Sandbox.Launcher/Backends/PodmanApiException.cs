namespace Trax.Scheduler.Sandbox.Launcher.Backends;

/// <summary>The podman API answered a call with an error.</summary>
internal sealed class PodmanApiException(int status, string message)
    : Exception(
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"podman answered {status}: {message}"
        )
    )
{
    public int Status { get; } = status;
}
