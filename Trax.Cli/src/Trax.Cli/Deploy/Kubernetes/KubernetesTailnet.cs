using System.Globalization;
using System.Security.Cryptography;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy.Kubernetes;

/// <summary>
/// What a <c>"exposure": "tailnet"</c> environment adds around its hub. Tailscale serve proxies a tailnet caller's
/// request and fills in who they are (<c>Tailscale-User-Login</c>), but it cannot add a header of trax's own, so a
/// small Caddy proxy (the edge) sits between it and the hub and sets <see cref="DeployExposure.ProxySecretHeader"/> to
/// a secret minted on every deploy, replacing any copy the caller sent. The hub's tailnet sign-in trusts the identity
/// headers only on a request that carries that secret.
/// <para>
/// On a cluster: the Tailscale operator's Ingress (its proxy pod runs Tailscale serve) in front of the edge's
/// Deployment and Service, a NetworkPolicy that admits only that proxy pod to the edge, and the edge to the hub. Under
/// podman: Tailscale and the edge as sidecars in the hub's pod, the edge bound to 127.0.0.1, and no port published on
/// the host.
/// </para>
/// </summary>
internal static class KubernetesTailnet
{
    /// <summary>The edge's name: its Deployment, Service, ConfigMap and Ingress on a cluster, its container under podman.</summary>
    public const string EdgeName = "tailnet";

    /// <summary>The port the edge listens on.</summary>
    public const int EdgePort = 8081;

    /// <summary>The edge's image, pinned. Caddy's binary carries only a permitted capability, so it runs with every one dropped.</summary>
    public const string CaddyImage = "docker.io/library/caddy:2.11.9";

    /// <summary>The Tailscale sidecar's image under podman, pinned.</summary>
    public const string TailscaleImage = "docker.io/tailscale/tailscale:v1.104.7";

    /// <summary>The ingress class the Tailscale operator serves.</summary>
    public const string IngressClass = "tailscale";

    /// <summary>The key the proxy secret is held under in <see cref="ProxySecretName"/>.</summary>
    public const string ProxySecretKey = "proxy-secret";

    /// <summary>The variable the edge reads the proxy secret from.</summary>
    public const string EdgeSecretVariable = "TRAX_PROXY_SECRET";

    /// <summary>The key in the app's Secret (from <c>.env</c> under podman) that holds the Tailscale auth key.</summary>
    public const string AuthKeySecretKey = "TAILSCALE_AUTH_KEY";

    /// <summary>The label that marks the edge's pods, which the policy for the app's own pods leaves out.</summary>
    public const string RoleLabel = "trax/role";

    public const string EdgeRole = "edge";

    /// <summary>
    /// The pod template annotation that changes on every deploy, so the edge restarts and reads the secret minted for
    /// it; the hub restarts anyway on its new image.
    /// </summary>
    public const string GenerationAnnotation = "trax/tailnet-generation";

    /// <summary>The user the edge runs as: not root, and owning nothing in the image.</summary>
    public const long EdgeUser = 65532;

    /// <summary>The Secret trax writes the proxy secret to: <c>&lt;app&gt;-&lt;env&gt;-tailnet</c>.</summary>
    public static string ProxySecretName(DeployFile file, string environment) =>
        $"{file.StackName(environment)}-{EdgeName}";

    /// <summary>The hub's machine name on the tailnet.</summary>
    public static string Hostname(DeployFile file, string environment, DeployEnvironment env) =>
        env.TailnetOptions?.Hostname ?? file.StackName(environment);

    public static string OperatorNamespace(DeployEnvironment env) =>
        env.TailnetOptions?.OperatorNamespace ?? DeployEnvironmentTailnet.DefaultOperatorNamespace;

    public static bool Exposed(DeployEnvironment env) =>
        string.Equals(env.Exposure, DeployEnvironment.Tailnet, StringComparison.Ordinal);

    /// <summary>A new proxy secret: 32 random bytes, base64url, 43 characters.</summary>
    public static string MintSecret() =>
        Convert
            .ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    /// <summary>
    /// The edge's Caddyfile: no admin endpoint, plain HTTP on <see cref="EdgePort"/> (on <paramref name="bind"/> only,
    /// when given), every request to <paramref name="upstream"/> with the secret set from the environment, replacing any
    /// copy the caller sent, so the secret is in no file.
    /// </summary>
    public static string Caddyfile(string? bind, string upstream)
    {
        var lines = new List<string>
        {
            "{",
            "    admin off",
            "    persist_config off",
            "    auto_https off",
            "}",
            "",
            string.Create(CultureInfo.InvariantCulture, $"http://:{EdgePort} {{"),
        };
        if (bind is not null)
            lines.Add($"    bind {bind}");
        lines.Add($"    reverse_proxy {upstream} {{");
        lines.Add(
            $"        header_up {DeployExposure.ProxySecretHeader} {{env.{EdgeSecretVariable}}}"
        );
        lines.Add("    }");
        lines.Add("}");
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>
    /// The Tailscale serve configuration of the podman sidecar: HTTPS on 443 with the tailnet's certificate, to the
    /// edge on 127.0.0.1. <c>${TS_CERT_DOMAIN}</c> is filled in by the container when it starts.
    /// </summary>
    public static string ServeConfig() =>
        "{\"TCP\":{\"443\":{\"HTTPS\":true}},\"Web\":{\"${TS_CERT_DOMAIN}:443\":{\"Handlers\":{\"/\":"
        + string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"Proxy\":\"http://127.0.0.1:{EdgePort}\"}}}}}}}}}}\n"
        );
}
