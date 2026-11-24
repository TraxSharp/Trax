using System.Text.Json.Serialization;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy;

/// <summary>One environment: a stack or a namespace of its own, on the target it names.</summary>
internal sealed class DeployEnvironment
{
    public const string Production = "production";
    public const string Try = "try";

    public const string Compose = "compose";
    public const string Aws = "aws";
    public const string Kubernetes = "kubernetes";

    public const string Podman = "podman";
    public const string Docker = "docker";

    public const string Cluster = "cluster";

    public const string Tailnet = "tailnet";
    public const string Public = "public";

    /// <summary>Where the environment runs: <c>compose</c>, <c>aws</c> (ECS Fargate) or <c>kubernetes</c>.</summary>
    public string? Target { get; set; }

    /// <summary>The engine a compose environment's stack is written for: <c>podman</c> (the default) or <c>docker</c>.</summary>
    public string? Engine { get; set; }

    /// <summary>
    /// Where a kubernetes environment's manifests run: <c>cluster</c> (the default), applied to the current kubeconfig
    /// context, or <c>podman</c>, written as pod YAML and Quadlet units for one host to run under systemd.
    /// </summary>
    public string? Runtime { get; set; }

    /// <summary>A kubernetes environment's namespace. Without it, <c>&lt;app&gt;-&lt;environment&gt;</c>.</summary>
    public string? Namespace { get; set; }

    /// <summary>
    /// The registry a kubernetes environment's images are pushed to and pulled from (<c>ghcr.io/acme</c>). Without
    /// it the images stay in the local container runtime, for a cluster that reads it.
    /// </summary>
    public string? Registry { get; set; }

    /// <summary>
    /// A kubernetes environment on EKS: the cluster, and the AWS foundation trax deploys beside it for its database
    /// and secrets.
    /// </summary>
    [JsonPropertyName("aws")]
    public DeployEnvironmentAws? Eks { get; set; }

    /// <summary>
    /// What reaches a kubernetes environment's hub from outside: <c>tailnet</c>, only the people and devices on the
    /// user's tailnet, through a Tailscale proxy trax renders (the Tailscale operator's Ingress on a cluster, a sidecar
    /// in the hub's pod under podman). Without it nothing outside the cluster reaches the hub, and under podman the hub
    /// is published on 127.0.0.1 only. <c>public</c> is not offered yet.
    /// </summary>
    public string? Exposure { get; set; }

    /// <summary>How a <c>tailnet</c> exposure is named and reached.</summary>
    [JsonPropertyName("tailnet")]
    public DeployEnvironmentTailnet? TailnetOptions { get; set; }

    /// <summary>A kubernetes environment's Postgres: an existing one named by secret, or one in the cluster.</summary>
    public DeployEnvironmentDatabase? Database { get; set; }

    /// <summary>
    /// On aws, <c>production</c>: private subnets behind a NAT gateway, a domain with its certificate, a Multi-AZ
    /// database kept on delete. <c>try</c>: public subnets locked by security groups, a CloudFront address, a small
    /// database deleted with the stack. On kubernetes, only <c>try</c> may run Postgres and RabbitMQ in the cluster.
    /// </summary>
    public string Profile { get; set; } = Production;

    /// <summary>An aws environment's region (<c>us-west-2</c>).</summary>
    public string? Region { get; set; }

    /// <summary>The host name the app serves on (<c>orders.example.com</c>). Production only.</summary>
    public string? Domain { get; set; }

    /// <summary>
    /// The Route 53 hosted zone that holds <see cref="Domain"/>. With it the certificate is validated and the name
    /// pointed at the load balancer automatically; without it trax prints the records to add.
    /// </summary>
    public string? HostedZoneId { get; set; }

    /// <summary>An existing VPC to deploy into, in place of the one the stack creates.</summary>
    public DeployNetwork? Network { get; set; }

    /// <summary>
    /// A Secrets Manager secret holding the hosts' whole Postgres connection string, in place of the database the
    /// stack creates.
    /// </summary>
    public string? DatabaseSecretArn { get; set; }

    /// <summary>The sign-in the stack creates: <c>cognito</c>, or none when the hosts trust an existing issuer.</summary>
    public DeployAuth? Auth { get; set; }

    /// <summary>
    /// Limits on the services trax runs beside the app, by name: <c>database</c> and <c>rabbitmq</c>. On compose, and
    /// on kubernetes where they run in the cluster; aws runs neither.
    /// </summary>
    public SortedDictionary<string, DeployLimits> Limits { get; set; } =
        new(StringComparer.Ordinal);

    /// <summary>Copies per service in this environment, over the file's <c>count</c>.</summary>
    public SortedDictionary<string, int> Counts { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// How long a deploy waits for busy work after it drains the hosts and before it replaces them, as a .NET time span
    /// (<c>00:10:00</c>). Ten minutes when not set. Past it the deploy refuses and lists what is still busy, unless it
    /// was run with <c>--force</c>.
    /// </summary>
    public string? DrainTimeout { get; set; }

    /// <summary>
    /// <see cref="DrainTimeout"/> as a time span, or <see cref="TraxDrain.DefaultTimeout"/> when it is not set. Throws
    /// <see cref="InvalidOperationException"/> for a value that is not a time span from one second to 24 hours.
    /// </summary>
    public TimeSpan DrainTimeoutValue()
    {
        if (DrainTimeout is null)
            return TraxDrain.DefaultTimeout;
        try
        {
            return TraxDrain.ParseTimeout(DrainTimeout, nameof(DrainTimeout));
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(
                $"drainTimeout '{DrainTimeout}' is not a time span from 00:00:01 to 1.00:00:00. Write it as hours, "
                    + "minutes and seconds, as in \"00:10:00\" for ten minutes.",
                ex
            );
        }
    }

    /// <summary>A value for each name the file lists under <c>settings</c>.</summary>
    public SortedDictionary<string, string> Settings { get; set; } = new(StringComparer.Ordinal);
}
