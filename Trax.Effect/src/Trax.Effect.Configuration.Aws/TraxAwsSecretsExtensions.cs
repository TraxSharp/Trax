using System.Data.Common;
using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Effect.Configuration.Aws;

/// <summary>
/// Reads a host's secrets from AWS Secrets Manager into its configuration before the host is built, with the
/// credentials the process already has: on EKS, the Pod Identity role <c>trax deploy</c> gives the pod. Which secrets
/// to read, and where each goes, is listed under <see cref="Section"/>, which <c>trax deploy</c> writes; the values
/// never pass through Kubernetes.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/configuration/add-trax-aws-secrets">SDK reference</seealso>
public static class TraxAwsSecretsExtensions
{
    /// <summary>
    /// The list of secrets to read. Each entry has a <c>SecretId</c> (a name or ARN), an optional <c>JsonKey</c> when
    /// the secret is a JSON object and one property of it is wanted, and where the value goes: <c>Key</c>, a
    /// configuration key (<c>Stripe:ApiKey</c>, or <c>Stripe__ApiKey</c> as an environment variable names it), or
    /// <c>PasswordOf</c>, the name of a connection string whose <c>Password</c> it sets.
    /// </summary>
    public const string Section = "Trax:AwsSecrets";

    /// <summary>The name this reader reports itself by in the host's deploy manifest.</summary>
    public const string SourceName = "aws-secrets";

    /// <summary>
    /// Reads every secret listed under <see cref="Section"/> and adds them to <paramref name="configuration"/>, above
    /// the sources already in it. Under <c>--trax-export-deploy</c> it reads none and only reports that the host reads
    /// its secrets from AWS, so an export needs no AWS credentials. Throws <see cref="InvalidOperationException"/>
    /// naming the entry when a secret, or the JSON property an entry names, cannot be read.
    /// </summary>
    /// <param name="configuration">The host builder's configuration.</param>
    /// <param name="client">The Secrets Manager client; null builds one from the process's credentials and region.</param>
    /// <param name="cancellationToken">Stops the reads.</param>
    /// <returns>The same configuration, for chaining.</returns>
    public static async Task<IConfigurationManager> AddTraxAwsSecretsAsync(
        this IConfigurationManager configuration,
        IAmazonSecretsManager? client = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (TraxDeployExport.Requested)
            return Report(configuration, []);
        if (client is not null)
            return await AddAsync(configuration, Reader(client), cancellationToken)
                .ConfigureAwait(false);
        using var owned = new AmazonSecretsManagerClient();
        return await AddAsync(configuration, Reader(owned), cancellationToken)
            .ConfigureAwait(false);
    }

    private static Func<string, CancellationToken, Task<string?>> Reader(
        IAmazonSecretsManager client
    ) =>
        async (id, ct) =>
            (
                await client
                    .GetSecretValueAsync(new GetSecretValueRequest { SecretId = id }, ct)
                    .ConfigureAwait(false)
            ).SecretString;

    /// <summary>
    /// <see cref="AddTraxAwsSecretsAsync"/> with the read itself passed in: <paramref name="read"/> returns a secret's
    /// string value by id.
    /// </summary>
    internal static async Task<IConfigurationManager> AddAsync(
        IConfigurationManager configuration,
        Func<string, CancellationToken, Task<string?>> read,
        CancellationToken cancellationToken
    )
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var cache = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var entry in configuration.GetSection(Section).GetChildren())
        {
            var id = entry["SecretId"] is { Length: > 0 } secretId
                ? secretId
                : throw Problem(entry, "names no SecretId");
            if (!cache.TryGetValue(id, out var raw))
                cache[id] = raw = await read(id, cancellationToken).ConfigureAwait(false);
            var value = Pick(entry, id, raw);
            if (entry["PasswordOf"] is { Length: > 0 } connection)
            {
                var key = $"ConnectionStrings:{connection}";
                var builder = new DbConnectionStringBuilder
                {
                    ConnectionString =
                        values.GetValueOrDefault(key)
                        ?? configuration[key]
                        ?? throw Problem(entry, $"sets the password of {key}, which is not set"),
                };
                builder["Password"] = value;
                values[key] = builder.ConnectionString;
            }
            else if (entry["Key"] is { Length: > 0 } target)
                values[target.Replace("__", ":", StringComparison.Ordinal)] = value;
            else
                throw Problem(entry, "names neither Key nor PasswordOf");
        }
        return Report(configuration, values);
    }

    private static string Pick(IConfigurationSection entry, string id, string? raw)
    {
        if (raw is null)
            throw Problem(entry, $"reads secret {id}, which holds no string value");
        if (entry["JsonKey"] is not { Length: > 0 } property)
            return raw;
        try
        {
            using var json = JsonDocument.Parse(raw);
            return
                json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty(property, out var found)
                && found.ValueKind == JsonValueKind.String
                ? found.GetString() ?? ""
                : throw Problem(
                    entry,
                    $"reads {property} from secret {id}, which has no such string"
                );
        }
        catch (JsonException)
        {
            throw Problem(entry, $"reads {property} from secret {id}, which is not JSON");
        }
    }

    private static IConfigurationManager Report(
        IConfigurationManager configuration,
        Dictionary<string, string?> values
    )
    {
        values[$"{DeployManifest.SecretSourcesSection}:{SourceName}"] = "true";
        configuration.AddInMemoryCollection(values);
        return configuration;
    }

    private static InvalidOperationException Problem(IConfigurationSection entry, string what) =>
        new(
            $"{entry.Path} {what}. "
                + "trax deploy writes these entries; deploy again, or check the secret exists and the host's role may "
                + "read it."
        );
}
