using Trax.Scheduler.Sandbox.Launcher.Configuration;

namespace Trax.Scheduler.Sandbox.Tests.Fakes;

/// <summary>Launcher options that pass validation, for a test to change one thing in.</summary>
public static class LauncherOptions
{
    public const string Image =
        "localhost/escape@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    public const string Holder =
        "localhost/pause@sha256:fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210";

    public const string Launcher =
        "localhost/launcher@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public const string StorageClass = "topolvm-provisioner";

    public static SandboxLauncherOptions Podman()
    {
        var options = new SandboxLauncherOptions
        {
            SigningKey = SandboxHosts.Key,
            ListenUrl = "http://127.0.0.1:7300",
            Backend = SandboxBackendKind.Podman,
        };
        options.Profiles["escape"] = new SandboxProfile { Image = Image };
        options.Podman.SocketPath = "/run/user/1000/podman/podman.sock";
        options.Podman.HolderImage = Holder;
        return options;
    }

    public static SandboxLauncherOptions Kubernetes()
    {
        var options = Podman();
        options.Backend = SandboxBackendKind.Kubernetes;
        options.Kubernetes.NodeName = "node-a";
        options.Kubernetes.OutputStorageClassName = StorageClass;
        options.Kubernetes.CollectorImage = Launcher;
        return options;
    }
}
