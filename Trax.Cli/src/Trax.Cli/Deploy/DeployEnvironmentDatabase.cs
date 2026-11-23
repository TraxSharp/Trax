namespace Trax.Cli.Deploy;

/// <summary>
/// A kubernetes environment's Postgres: the Secret and key holding an existing database's whole connection string, or
/// <c>inCluster</c> for a StatefulSet that the production profile refuses.
/// </summary>
internal sealed class DeployEnvironmentDatabase
{
    public string? Secret { get; set; }

    public string? Key { get; set; }

    public bool? InCluster { get; set; }
}
