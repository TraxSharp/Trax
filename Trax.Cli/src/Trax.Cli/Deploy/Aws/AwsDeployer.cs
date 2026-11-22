using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy.Aws;

/// <summary>
/// Puts an environment on AWS: checks its secrets, pushes the images, then changes the stack in two passes around
/// the migration, so no service of the new version starts before its schema exists.
/// </summary>
internal sealed class AwsDeployer(IAwsDeployClient aws, IImagePublisher images, Action<string> say)
{
    /// <summary>CloudFormation's limit on a template sent inline.</summary>
    public const int InlineTemplateLimit = 51_200;

    public async Task<JsonObject> PlanAsync(
        DeployFile file,
        string environment,
        IReadOnlyDictionary<string, DeployManifest> exports,
        CancellationToken ct
    )
    {
        var env = file.Environment(environment);
        var prefixList = string.Equals(env.Profile, DeployEnvironment.Try, StringComparison.Ordinal)
            ? await aws.CloudFrontPrefixListAsync(ct).ConfigureAwait(false)
            : null;
        return AwsStack.Build(file, environment, exports, prefixList);
    }

    /// <summary>Deploys <paramref name="environment"/> and returns the address it serves on, if it serves one.</summary>
    public async Task<string?> DeployAsync(
        DeployFile file,
        string filePath,
        string environment,
        IReadOnlyDictionary<string, DeployManifest> exports,
        string tag,
        CancellationToken ct
    )
    {
        var env = file.Environment(environment);
        var stack = file.StackName(environment);
        var tags = Tags(file, environment);
        var existing = await aws.DescribeStackAsync(stack, ct);
        // A first deploy that stopped before its change set ran left an empty stack in review: create it again.
        if (existing is { Created: false })
            existing = null;
        if (existing is not null)
            CheckOwner(existing, file, stack);

        // Nothing is created until every secret the app needs has a value.
        var stored = (
            await aws.ListSecretsAsync(AwsStack.AppSecretName(stack, ""), ct).ConfigureAwait(false)
        )
            .Select(s => s.Name)
            .ToHashSet(StringComparer.Ordinal);
        var missing = file
            .Secrets.Where(s => !stored.Contains(AwsStack.AppSecretName(stack, s)))
            .ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"{stack} has no value for {string.Join(", ", missing)}. Set each with "
                    + $"'trax deploy secret set <name> --env {environment}'."
            );

        if (existing is { Dead: true })
            throw new InvalidOperationException(
                $"{stack} is {existing.Status}: its creation failed and it can only be deleted. Run "
                    + $"'trax deploy destroy --env {environment}', then deploy again."
            );
        if (existing is { Settled: false })
            throw new InvalidOperationException(
                $"{stack} is {existing.Status}. Wait for it to settle, then deploy again."
            );

        var template = AwsStack.Compact(
            await PlanAsync(file, environment, exports, ct).ConfigureAwait(false)
        );
        if (template.Length > InlineTemplateLimit)
            throw new InvalidOperationException(
                $"The template is {template.Length} bytes, over CloudFormation's {InlineTemplateLimit}-byte limit "
                    + "for a template sent inline."
            );

        say($"Pushing images tagged {tag}...");
        var login = await aws.RegistryLoginAsync(ct).ConfigureAwait(false);
        var image = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, service) in file.Services)
        {
            var repository = await aws.EnsureRepositoryAsync(Repository(stack, name), tags, ct)
                .ConfigureAwait(false);
            say($"  {name} -> {repository}:{tag}");
            await images
                .PublishAsync(DeployFile.ProjectPath(filePath, service), repository, tag, login)
                .ConfigureAwait(false);
            image[name] = $"{repository}:{tag}";
        }
        var migrator = image[AwsStack.Migrator(file, exports)];

        // Pass 1: the infrastructure, and the migrate task on the new image. A service the stack does not run yet
        // (every service of a new stack) waits at zero on the new image; one it runs keeps its image and count.
        var first = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [AwsStack.MigrateImageParameter] = migrator,
        };
        foreach (var name in file.Services.Keys)
        {
            var (count, current) = Holding(existing, file, env, name);
            first[AwsStack.CountParameter(name)] = count;
            first[AwsStack.ImageParameter(name)] = current ? null : image[name];
        }
        if (existing?.Parameters.ContainsKey(AwsStack.ProtectParameter) is true)
            first[AwsStack.ProtectParameter] = "true";
        if (KeepsItsUserPool(env))
            first[AwsStack.UserPoolParameter] = await UserPoolAsync(stack, existing, ct);
        say(existing is null ? $"Creating {stack}..." : $"Updating {stack}...");
        await aws.DeployAsync(stack, template, first, tags, say, ct).ConfigureAwait(false);

        var outputs = (await aws.DescribeStackAsync(stack, ct).ConfigureAwait(false))
            .NotNull()
            .Outputs;
        say("Migrating the database...");
        var migration = await aws.RunTaskAsync(
                new AwsTaskRequest(
                    outputs["ClusterName"],
                    outputs["MigrateTaskDefinition"],
                    AwsStack.MigrateContainer,
                    outputs["ServiceSubnets"].Split(','),
                    outputs["TaskSecurityGroup"],
                    string.Equals(outputs["AssignPublicIp"], "ENABLED", StringComparison.Ordinal)
                ),
                say,
                ct
            )
            .ConfigureAwait(false);
        if (migration.ExitCode != 0)
            throw new InvalidOperationException(
                $"The migration ended {(migration.ExitCode is null ? "without an exit code" : string.Create(CultureInfo.InvariantCulture, $"with exit code {migration.ExitCode}"))}"
                    + (migration.StoppedReason is null ? "" : $" ({migration.StoppedReason})")
                    + ", so no service was moved to the new version. Its log is "
                    + $"{AwsStack.LogGroup(stack, AwsStack.MigrateContainer)} {migration.LogStream}: "
                    + $"'trax deploy logs migrate --env {environment}'."
            );

        // Pass 2: every service onto the new image at its count, rolled with the circuit breaker on.
        var second = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [AwsStack.MigrateImageParameter] = migrator,
        };
        foreach (var name in file.Services.Keys)
        {
            second[AwsStack.CountParameter(name)] = file.Count(env, name)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            second[AwsStack.ImageParameter(name)] = image[name];
        }
        if (first.ContainsKey(AwsStack.ProtectParameter))
            second[AwsStack.ProtectParameter] = "true";
        if (first.TryGetValue(AwsStack.UserPoolParameter, out var pool))
            second[AwsStack.UserPoolParameter] = pool;
        say($"Rolling the services onto {tag}...");
        await aws.DeployAsync(stack, template, second, tags, say, ct).ConfigureAwait(false);

        return (await aws.DescribeStackAsync(stack, ct).ConfigureAwait(false))
            .NotNull()
            .Outputs.GetValueOrDefault("Url");
    }

    /// <summary>Whether the environment's user pool outlives its stack: production's Cognito pool.</summary>
    private static bool KeepsItsUserPool(DeployEnvironment env) =>
        env.Profile == DeployEnvironment.Production && env.Auth is { Kind: DeployAuth.Cognito };

    /// <summary>
    /// The pool a deploy passes the stack: the one the stack already uses, or on a new stack the one an earlier
    /// destroy kept, or empty to create one.
    /// </summary>
    private async Task<string> UserPoolAsync(
        string stack,
        AwsStackState? existing,
        CancellationToken ct
    )
    {
        if (existing is not null)
            // A stack keeps the pool it has: switching would take the one it created out of the stack.
            return existing.Parameters.GetValueOrDefault(AwsStack.UserPoolParameter) ?? "";
        var kept = await aws.GetParameterAsync(UserPoolRecord(stack), ct);
        if (kept is not null)
            say($"Signing in against the user pool {kept}, which the last destroy kept.");
        return kept ?? "";
    }

    /// <summary>
    /// Deletes the environment's stack. Production's database is unprotected first and keeps a final snapshot, and
    /// production's user pool is kept and recorded for the next deploy; with <paramref name="deleteData"/> the pool,
    /// the images and the app's secrets go too.
    /// </summary>
    public async Task DestroyAsync(
        DeployFile file,
        string environment,
        bool deleteData,
        CancellationToken ct
    )
    {
        var env = file.Environment(environment);
        var stack = file.StackName(environment);
        var existing = await aws.DescribeStackAsync(stack, ct).ConfigureAwait(false);
        var record = UserPoolRecord(stack);
        var pool = KeepsItsUserPool(env)
            ? existing?.Outputs.GetValueOrDefault("UserPoolId")
                ?? await aws.GetParameterAsync(record, ct).ConfigureAwait(false)
            : null;
        // Recorded before the stack goes, so a destroy that stops partway still leaves the pool findable.
        if (pool is not null && !deleteData)
            await aws.PutParameterAsync(record, pool, Tags(file, environment), ct)
                .ConfigureAwait(false);
        if (existing is null)
            say($"{stack} does not exist.");
        else
        {
            CheckOwner(existing, file, stack);
            if (existing.Parameters.ContainsKey(AwsStack.ProtectParameter) && !existing.Dead)
            {
                say("Turning off the database's deletion protection...");
                var keep = existing.Parameters.Keys.ToDictionary(
                    k => k,
                    k =>
                        string.Equals(k, AwsStack.ProtectParameter, StringComparison.Ordinal)
                            ? "false"
                            : (string?)null,
                    StringComparer.Ordinal
                );
                await aws.DeployAsync(stack, template: null, keep, Tags(file, environment), say, ct)
                    .ConfigureAwait(false);
            }
            say($"Deleting {stack}...");
            await aws.DeleteStackAsync(stack, say, ct).ConfigureAwait(false);
            if (existing.Parameters.ContainsKey(AwsStack.ProtectParameter))
                say(
                    "The database left a final snapshot, which RDS keeps until you delete it (RDS console, "
                        + "Snapshots)."
                );
        }
        if (pool is not null)
        {
            if (deleteData)
            {
                await aws.DeleteUserPoolAsync(pool, ct);
                await aws.DeleteParameterAsync(record, ct);
                say($"Deleted the user pool {pool} and every user in it.");
            }
            else
            {
                var users = await aws.UserCountAsync(pool, ct);
                say(
                    users is null
                        ? $"The user pool {pool} recorded in {record} no longer exists; the next deploy creates one."
                        : $"Kept the user pool {pool} with its {users} users (Cognito's estimate), recorded in "
                            + $"{record}. The next deploy of {environment} signs in against it; "
                            + $"'trax deploy destroy --env {environment} --delete-data' deletes it."
                );
                if (users is null)
                    await aws.DeleteParameterAsync(record, ct);
            }
        }
        if (!deleteData)
            return;
        foreach (var name in file.Services.Keys)
            await aws.DeleteRepositoryAsync(Repository(stack, name), ct).ConfigureAwait(false);
        foreach (var name in file.Secrets)
            await aws.DeleteSecretAsync(AwsStack.AppSecretName(stack, name), ct)
                .ConfigureAwait(false);
        say(
            "Deleted the images and the app's secrets. For seven days 'trax deploy secret set' restores a secret "
                + "with its new value."
        );
    }

    /// <summary>
    /// What pass 1 sends for <paramref name="service"/>: its count (null keeps the stack's), and whether it stays on
    /// the image it runs. A service the stack holds no image for, or runs none of, waits at zero on the new image.
    /// A stack from before the count parameters says whether its services run in its "Scale" parameter.
    /// </summary>
    private static (string? Count, bool Current) Holding(
        AwsStackState? existing,
        DeployFile file,
        DeployEnvironment env,
        string service
    )
    {
        if (existing?.Parameters.ContainsKey(AwsStack.ImageParameter(service)) is not true)
            return ("0", false);
        if (existing.Parameters.TryGetValue(AwsStack.CountParameter(service), out var count))
            return string.Equals(count, "0", StringComparison.Ordinal)
                ? ("0", false)
                : (null, true);
        return string.Equals(
            existing.Parameters.GetValueOrDefault("Scale"),
            "0",
            StringComparison.Ordinal
        )
            ? ("0", false)
            : (
                file.Count(env, service)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture),
                true
            );
    }

    /// <summary>
    /// Refuses a stack another app deployed. An app and an environment name can join to the same stack name as
    /// another app's (<c>orders-eu</c> + <c>prod</c>, <c>orders</c> + <c>eu-prod</c>), and changing that stack would
    /// move the other app's services onto this app's images.
    /// </summary>
    private static void CheckOwner(AwsStackState existing, DeployFile file, string stack)
    {
        var owner = existing.Tags.GetValueOrDefault("trax:app");
        if (owner == file.App)
            return;
        throw new InvalidOperationException(
            owner is null
                ? $"{stack} has no trax:app tag, so trax deploy did not create it. trax changes only the stacks it "
                    + "created."
                : $"{stack} belongs to app '{owner}', not '{file.App}'. Rename this app or its environment."
        );
    }

    /// <summary>
    /// The SSM parameter a destroy records a kept user pool's id in, which the next deploy of the environment reads:
    /// <c>/&lt;stack&gt;/user-pool</c>.
    /// </summary>
    public static string UserPoolRecord(string stack) => $"/{stack}/user-pool";

    /// <summary>The ECR repository of a service: <c>trax/&lt;stack&gt;/&lt;service&gt;</c>.</summary>
    public static string Repository(string stack, string service) => $"trax/{stack}/{service}";

    public static IReadOnlyDictionary<string, string> Tags(DeployFile file, string environment) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["trax:app"] = file.App,
            ["trax:environment"] = environment,
        };

    /// <summary>
    /// A tag no earlier deploy used: the time, then the commit and whether the tree had changes, so an image says
    /// what it was built from.
    /// </summary>
    public static async Task<string> ImageTagAsync(string directory, DateTime now)
    {
        var tag = now.ToString(
            "yyyyMMdd-HHmmss",
            System.Globalization.CultureInfo.InvariantCulture
        );
        var commit = await GitAsync(directory, "rev-parse", "--short=12", "HEAD")
            .ConfigureAwait(false);
        if (commit is null)
            return tag;
        var dirty =
            await GitAsync(directory, "status", "--porcelain").ConfigureAwait(false) is
            { Length: > 0 };
        return $"{tag}-{commit}{(dirty ? "-dirty" : "")}";
    }

    private static async Task<string?> GitAsync(string directory, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        try
        {
            var (exit, stdout, _) = await ChildProcess.RunAsync(psi).ConfigureAwait(false);
            return exit == 0 ? stdout.Trim() : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
