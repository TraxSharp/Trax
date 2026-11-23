using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Trax.Cli.Deploy;

/// <summary>One cloud environment: a stack of its own.</summary>
internal sealed class DeployEnvironment
{
    public const string Production = "production";
    public const string Try = "try";

    /// <summary>
    /// <c>production</c>: private subnets behind a NAT gateway, a domain with its certificate, a Multi-AZ database
    /// kept on delete. <c>try</c>: public subnets locked by security groups, a CloudFront address, a small database
    /// deleted with the stack.
    /// </summary>
    public string Profile { get; set; } = Production;

    public string Region { get; set; } = "";

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

    /// <summary>Copies per service in this environment, over the file's <c>count</c>.</summary>
    public SortedDictionary<string, int> Counts { get; set; } = new(StringComparer.Ordinal);

    /// <summary>A value for each name the file lists under <c>settings</c>.</summary>
    public SortedDictionary<string, string> Settings { get; set; } = new(StringComparer.Ordinal);
}
