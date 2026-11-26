namespace Trax.Scheduler.Sandbox.Launcher.Backends;

/// <summary>The names of everything one podman run creates, all derived from its run id.</summary>
internal sealed record PodmanRunNames(string RunId)
{
    public string Sandbox => $"trax-sbx-{RunId}";
    public string Pod => $"trax-sbx-{RunId}-pod";
    public string Holder => $"trax-sbx-{RunId}-holder";
    public string Input => $"trax-sbx-{RunId}-in";
    public string Output => $"trax-sbx-{RunId}-out";
}
