using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Trax.Effect.Configuration.Deploy;

/// <summary>
/// One thing a host needs from where it is deployed, recorded by the registration that adds it: its data provider, a
/// role (<c>scheduler</c>, <c>worker</c>, <c>job-runner</c>), a transport. Collected into the
/// <see cref="DeployManifest"/> a deploy export writes.
/// </summary>
[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(
    System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.All
)]
internal sealed record DeployFact(string Kind, string Value)
{
    public const string Database = "database";
    public const string MigratesOnStart = "migrates-on-start";
    public const string Role = "role";
    public const string Transport = "transport";

    /// <summary>Records a fact. Duplicates are harmless: the manifest keeps one of each.</summary>
    public static void Add(IServiceCollection services, string kind, string value) =>
        services.AddSingleton(new DeployFact(kind, value));
}
