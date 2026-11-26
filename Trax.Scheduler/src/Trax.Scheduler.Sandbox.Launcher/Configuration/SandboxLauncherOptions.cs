using Microsoft.Extensions.Configuration;
using Trax.Scheduler.Services.Sandbox;

namespace Trax.Scheduler.Sandbox.Launcher.Configuration;

/// <summary>
/// The launcher's whole configuration, read from <c>Trax:SandboxLauncher</c>. Everything that
/// shapes a sandbox is here, set at deploy time; nothing a hub sends can change it.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/scheduler-api/trax-sandbox-launcher">SDK reference</seealso>
public sealed class SandboxLauncherOptions
{
    /// <summary>The configuration section the launcher reads.</summary>
    public const string Section = "Trax:SandboxLauncher";

    /// <summary>The runtime every sandbox runs under. Kata by default.</summary>
    public SandboxRuntime Runtime { get; set; } = SandboxRuntime.Kata;

    /// <summary>Where sandboxes run.</summary>
    public SandboxBackendKind Backend { get; set; } = SandboxBackendKind.Podman;

    /// <summary>The key shared with the hub, at least 32 bytes; in configuration as base64 under <c>SigningKey</c>.</summary>
    public byte[]? SigningKey { get; set; }

    /// <summary>
    /// The Unix socket the launcher listens on, on one host, ending in <c>.sock</c>. Created with
    /// <see cref="ListenSocketMode"/>; an empty file left at the path by an earlier launcher is
    /// removed first.
    /// </summary>
    public string? ListenSocketPath { get; set; }

    /// <summary>The socket's permissions; owner and group read-write by default, so the hub's user or group can connect and nobody else.</summary>
    public UnixFileMode ListenSocketMode { get; set; } =
        UnixFileMode.UserRead
        | UnixFileMode.UserWrite
        | UnixFileMode.GroupRead
        | UnixFileMode.GroupWrite;

    /// <summary>The URL the launcher listens on, on a cluster: <c>http://</c>, an IP address and a port, such as <c>http://0.0.0.0:8080</c>.</summary>
    public string? ListenUrl { get; set; }

    /// <summary>The profiles a hub may name, by name.</summary>
    public IDictionary<string, SandboxProfile> Profiles { get; } =
        new Dictionary<string, SandboxProfile>(StringComparer.Ordinal);

    /// <summary>The host-wide caps.</summary>
    public SandboxCaps Caps { get; } = new();

    /// <summary>The podman backend, with <see cref="SandboxBackendKind.Podman"/>.</summary>
    public PodmanBackendOptions Podman { get; } = new();

    /// <summary>The Kubernetes backend, with <see cref="SandboxBackendKind.Kubernetes"/>.</summary>
    public KubernetesBackendOptions Kubernetes { get; } = new();

    /// <summary>
    /// More <c>host:port</c> pairs the startup canary must fail to reach, on top of the metadata
    /// endpoints and the cluster's API server: the hub, the database, anything a sandbox must never touch.
    /// </summary>
    public IList<string> CanaryProbes { get; } = [];

    /// <summary>Reads the options from <paramref name="configuration"/>'s <see cref="Section"/>.</summary>
    public static SandboxLauncherOptions Read(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(Section);
        var options = new SandboxLauncherOptions();
        section.Bind(options, o => o.ErrorOnUnknownConfiguration = true);
        if (section["SigningKey"] is { Length: > 0 } key)
        {
            try
            {
                options.SigningKey = Convert.FromBase64String(key);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException($"{Section}:SigningKey is not base64.", ex);
            }
        }
        if (string.IsNullOrEmpty(options.Kubernetes.NodeName))
            options.Kubernetes.NodeName = Environment.GetEnvironmentVariable("NODE_NAME") ?? "";
        return options;
    }

    /// <summary>Throws <see cref="InvalidOperationException"/> naming the first thing that would let a sandbox run unsafely or not at all.</summary>
    public void Validate()
    {
        var problem = Problem();
        if (problem is not null)
            throw new InvalidOperationException($"The sandbox launcher will not start: {problem}.");
    }

    private string? Problem()
    {
        if (SigningKey is null || SigningKey.Length < SandboxChannelSignature.MinimumKeyLength)
            return $"SigningKey must be at least {SandboxChannelSignature.MinimumKeyLength} bytes";
        if (string.IsNullOrEmpty(ListenSocketPath) == string.IsNullOrEmpty(ListenUrl))
            return "set exactly one of ListenSocketPath and ListenUrl";
        if (
            ListenSocketPath is not null
            && (
                !Path.IsPathFullyQualified(ListenSocketPath)
                || !ListenSocketPath.EndsWith(".sock", StringComparison.Ordinal)
            )
        )
            return "ListenSocketPath must be absolute and end in .sock";
        if (ListenUrl is not null && !IsListenUrl(ListenUrl))
            return "ListenUrl must be http://<IP address>:<port>";
        if (!Enum.IsDefined(Runtime) || !Enum.IsDefined(Backend))
            return "Runtime or Backend is not one the launcher knows";
        if (Profiles.Count == 0)
            return "no profile is configured";
        foreach (var (name, profile) in Profiles)
            if (profile.Invalid(name) is { } bad)
                return bad;
        if (Caps.Invalid() is { } caps)
            return caps;
        return Backend == SandboxBackendKind.Podman ? PodmanProblem() : KubernetesProblem();
    }

    private static bool IsListenUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
        && System.Net.IPAddress.TryParse(uri.Host.Trim('[', ']'), out _)
        && !uri.IsDefaultPort;

    private string? PodmanProblem() =>
        !Path.IsPathFullyQualified(Podman.SocketPath) ? "Podman:SocketPath must be absolute"
        : !SandboxProfile.IsPinned(Podman.HolderImage)
            ? "Podman:HolderImage must be pinned as name@sha256:<digest>"
        : null;

    private string? KubernetesProblem() =>
        !SandboxProfile.Name.IsMatch(Kubernetes.Namespace)
            ? "Kubernetes:Namespace is not a namespace name"
        : string.IsNullOrEmpty(Kubernetes.RuntimeClassName)
            ? "Kubernetes:RuntimeClassName is required"
        : string.IsNullOrEmpty(Kubernetes.RunsClaimName) ? "Kubernetes:RunsClaimName is required"
        : string.IsNullOrEmpty(Kubernetes.OutputStorageClassName)
            ? "Kubernetes:OutputStorageClassName is required: a StorageClass that enforces capacity, the run's disk ceiling"
        : KubernetesStorage.NamesAHostPathClass(Kubernetes.OutputStorageClassName)
            ? $"Kubernetes:OutputStorageClassName '{Kubernetes.OutputStorageClassName}' enforces no size"
        : !SandboxProfile.IsPinned(Kubernetes.CollectorImage)
            ? "Kubernetes:CollectorImage must be the launcher's image, pinned as name@sha256:<digest>"
        : !Path.IsPathFullyQualified(Kubernetes.RunsPath)
        || string.Equals(Path.GetFullPath(Kubernetes.RunsPath), "/", StringComparison.Ordinal)
            ? "Kubernetes:RunsPath must be an absolute path of its own, not the root"
        : string.IsNullOrEmpty(Kubernetes.NodeName)
            ? "Kubernetes:NodeName is required (set NODE_NAME from spec.nodeName)"
        : Kubernetes.PollInterval <= TimeSpan.Zero ? "Kubernetes:PollInterval must be positive"
        : Kubernetes.RunsMinFreePercent is < 1 or > 90
            ? "Kubernetes:RunsMinFreePercent must be from 1 to 90"
        : null;
}
