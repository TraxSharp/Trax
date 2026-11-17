using System.Diagnostics;
using System.Text.Json.Nodes;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy.Aws;

/// <summary>Builds a service's image and pushes it to a registry.</summary>
internal interface IImagePublisher
{
    void Publish(string projectPath, string repositoryUri, string tag, RegistryLogin login);
}

/// <summary>
/// Puts an environment on AWS: checks its secrets, pushes the images, brings the foundation (network, database,
/// secrets, sign-in) up to date, then changes the ECS runtime in two passes around the migration, so no service of
/// the new version starts before its schema exists.
/// </summary>
internal sealed class AwsDeployer(IAwsDeployClient aws, IImagePublisher images, Action<string> say)
{
    /// <summary>CloudFormation's limit on a template sent inline.</summary>
    public const int InlineTemplateLimit = 51_200;

    /// <summary>The tag naming which of an environment's two stacks a stack is.</summary>
    public const string PartTag = "trax:part";

    public const string FoundationPart = "foundation";
    public const string RuntimePart = "runtime";

    public async Task<AwsTemplates> PlanAsync(
        DeployFile file,
        string environment,
        IReadOnlyDictionary<string, DeployManifest> exports,
        CancellationToken ct
    )
    {
        var env = file.Environment(environment);
        var prefixList =
            env.Profile == DeployEnvironment.Try ? await aws.CloudFrontPrefixListAsync(ct) : null;
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
        var baseStack = file.FoundationStackName(environment);
        var foundation = await ExistingAsync(baseStack, file, FoundationPart, ct);
        var existing = await ExistingAsync(stack, file, RuntimePart, ct);

        // Nothing is created until every secret the app needs has a value.
        var stored = (await aws.ListSecretsAsync(AwsStack.AppSecretName(stack, ""), ct))
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

        foreach (var (name, state) in new[] { (baseStack, foundation), (stack, existing) })
        {
            if (state is { Dead: true })
                throw new InvalidOperationException(
                    $"{name} is {state.Status}: its creation failed and it can only be deleted. Run "
                        + $"'trax deploy destroy --env {environment}', then deploy again."
                );
            if (state is { Settled: false })
                throw new InvalidOperationException(
                    $"{name} is {state.Status}. Wait for it to settle, then deploy again."
                );
        }

        var templates = await PlanAsync(file, environment, exports, ct);
        var foundationTemplate = Inline(baseStack, templates.Foundation);
        var runtimeTemplate = Inline(stack, templates.Runtime);

        say($"Pushing images tagged {tag}...");
        var login = await aws.RegistryLoginAsync(ct);
        var image = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, service) in file.Services)
        {
            var repository = await aws.EnsureRepositoryAsync(
                Repository(stack, name),
                Tags(file, environment),
                ct
            );
            say($"  {name} -> {repository}:{tag}");
            images.Publish(DeployFile.ProjectPath(filePath, service), repository, tag, login);
            image[name] = $"{repository}:{tag}";
        }
        var migrator = image[AwsStack.Migrator(file, exports)];

        // The foundation first: the runtime reads what it outputs.
        var under = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (foundation?.Parameters.ContainsKey(AwsStack.ProtectParameter) is true)
            under[AwsStack.ProtectParameter] = "true";
        if (KeepsItsUserPool(env))
            under[AwsStack.UserPoolParameter] = await UserPoolAsync(stack, foundation, ct);
        say(foundation is null ? $"Creating {baseStack}..." : $"Updating {baseStack}...");
        await aws.DeployAsync(
            baseStack,
            foundationTemplate,
            under,
            StackTags(file, environment, FoundationPart),
            say,
            ct
        );
        var given = FoundationValues(
            (await aws.DescribeStackAsync(baseStack, ct))!.Outputs,
            templates.Runtime
        );

        // Pass 1: the runtime, and the migrate task on the new image. A service the stack does not run yet (every
        // service of a new stack) waits at zero on the new image; one it runs keeps its image and count.
        var first = new Dictionary<string, string?>(given, StringComparer.Ordinal)
        {
            [AwsStack.MigrateImageParameter] = migrator,
        };
        foreach (var name in file.Services.Keys)
        {
            var (count, current) = Holding(existing, name);
            first[AwsStack.CountParameter(name)] = count;
            first[AwsStack.ImageParameter(name)] = current ? null : image[name];
        }
        var tags = StackTags(file, environment, RuntimePart);
        say(existing is null ? $"Creating {stack}..." : $"Updating {stack}...");
        await aws.DeployAsync(stack, runtimeTemplate, first, tags, say, ct);

        var outputs = (await aws.DescribeStackAsync(stack, ct))!.Outputs;
        say("Migrating the database...");
        var migration = await aws.RunTaskAsync(
            new AwsTaskRequest(
                outputs["ClusterName"],
                outputs["MigrateTaskDefinition"],
                AwsStack.MigrateContainer,
                outputs["ServiceSubnets"].Split(','),
                outputs["TaskSecurityGroups"].Split(','),
                outputs["AssignPublicIp"] == "ENABLED"
            ),
            say,
            ct
        );
        if (migration.ExitCode != 0)
            throw new InvalidOperationException(
                $"The migration ended {(migration.ExitCode is null ? "without an exit code" : $"with exit code {migration.ExitCode}")}"
                    + (migration.StoppedReason is null ? "" : $" ({migration.StoppedReason})")
                    + ", so no service was moved to the new version. Its log is "
                    + $"{AwsStack.LogGroup(stack, AwsStack.MigrateContainer)} {migration.LogStream}: "
                    + $"'trax deploy logs migrate --env {environment}'."
            );

        // Pass 2: every service onto the new image at its count, rolled with the circuit breaker on.
        var second = new Dictionary<string, string?>(given, StringComparer.Ordinal)
        {
            [AwsStack.MigrateImageParameter] = migrator,
        };
        foreach (var name in file.Services.Keys)
        {
            second[AwsStack.CountParameter(name)] = file.Count(env, name)
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            second[AwsStack.ImageParameter(name)] = image[name];
        }
        say($"Rolling the services onto {tag}...");
        await aws.DeployAsync(stack, runtimeTemplate, second, tags, say, ct);

        return (await aws.DescribeStackAsync(stack, ct))!.Outputs.GetValueOrDefault("Url");
    }

    /// <summary>
    /// The stack as it stands, checked to be this app's and the expected part; null when there is none, or when a
    /// first deploy's change set never ran and left it empty in review (it is created again).
    /// </summary>
    private async Task<AwsStackState?> ExistingAsync(
        string stack,
        DeployFile file,
        string part,
        CancellationToken ct
    )
    {
        var state = await aws.DescribeStackAsync(stack, ct);
        if (state is null or { Created: false })
            return null;
        CheckOwner(state, file, stack);
        var found = state.Tags.GetValueOrDefault(PartTag);
        if (found == part)
            return state;
        throw new InvalidOperationException(
            found is null
                ? $"{stack} was deployed as one stack by an earlier trax, which now keeps the network, database and "
                    + "sign-in in a stack of their own. Run 'trax deploy destroy' for this environment (production "
                    + "keeps a final snapshot of the database and its user pool), then deploy again."
                : $"{stack} is a {found} stack where trax expects the {part}. Rename this app or its environment."
        );
    }

    /// <summary>The template as sent inline, refused when it is over CloudFormation's limit.</summary>
    private static string Inline(string stack, JsonObject template)
    {
        var text = AwsStack.Compact(template);
        if (text.Length > InlineTemplateLimit)
            throw new InvalidOperationException(
                $"The template of {stack} is {text.Length} bytes, over CloudFormation's {InlineTemplateLimit}-byte "
                    + "limit for a template sent inline."
            );
        return text;
    }

    /// <summary>The foundation's outputs the runtime template declares as parameters.</summary>
    private static Dictionary<string, string?> FoundationValues(
        IReadOnlyDictionary<string, string> outputs,
        JsonObject runtime
    )
    {
        var declared = (JsonObject)runtime["Parameters"]!;
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var name in AwsStack.FoundationValues.Where(declared.ContainsKey))
            values[name] = outputs.TryGetValue(name, out var value)
                ? value
                : throw new InvalidOperationException(
                    $"The foundation stack has no output {name}, which the services need. Deploy again."
                );
        return values;
    }

    /// <summary>Whether the environment's user pool outlives its stack: production's Cognito pool.</summary>
    private static bool KeepsItsUserPool(DeployEnvironment env) =>
        env.Profile == DeployEnvironment.Production && env.Auth is { Kind: DeployAuth.Cognito };

    /// <summary>
    /// The pool a deploy passes the foundation: the one it already uses, or on a new foundation the one an earlier
    /// destroy kept, or empty to create one.
    /// </summary>
    private async Task<string> UserPoolAsync(
        string stack,
        AwsStackState? foundation,
        CancellationToken ct
    )
    {
        if (foundation is not null)
            // A stack keeps the pool it has: switching would take the one it created out of the stack.
            return foundation.Parameters.GetValueOrDefault(AwsStack.UserPoolParameter) ?? "";
        var kept = await aws.GetParameterAsync(UserPoolRecord(stack), ct);
        if (kept is not null)
            say($"Signing in against the user pool {kept}, which the last destroy kept.");
        return kept ?? "";
    }

    /// <summary>
    /// Deletes the environment: the runtime, then the foundation. It refuses first, deleting nothing, when the
    /// foundation's network holds something trax did not create there, such as an EKS cluster: deleting the
    /// network would fail partway or strand it. Production's database is unprotected first and keeps a final
    /// snapshot, and production's user pool is kept and recorded for the next deploy; with
    /// <paramref name="deleteData"/> the pool, the images and the app's secrets go too.
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
        var baseStack = file.FoundationStackName(environment);
        var runtime = await aws.DescribeStackAsync(stack, ct);
        var foundation = await aws.DescribeStackAsync(baseStack, ct);
        if (runtime is not null)
            CheckOwner(runtime, file, stack);
        if (foundation is not null)
            CheckOwner(foundation, file, baseStack);
        if (foundation is { Dead: false })
            await CheckNetworkAsync(baseStack, foundation, runtime, ct);

        var record = UserPoolRecord(stack);
        var pool = KeepsItsUserPool(env)
            ? foundation?.Outputs.GetValueOrDefault("UserPoolId")
                // A single stack from before the split kept its pool in its own outputs.
                ?? runtime?.Outputs.GetValueOrDefault("UserPoolId")
                ?? await aws.GetParameterAsync(record, ct)
            : null;
        // Recorded before the stacks go, so a destroy that stops partway still leaves the pool findable.
        if (pool is not null && !deleteData)
            await aws.PutParameterAsync(record, pool, Tags(file, environment), ct);
        if (runtime is null && foundation is null)
            say($"{stack} does not exist.");
        var snapshot = false;
        foreach (var (name, state) in new[] { (stack, runtime), (baseStack, foundation) })
        {
            if (state is null)
                continue;
            if (state.Parameters.ContainsKey(AwsStack.ProtectParameter) && !state.Dead)
            {
                say("Turning off the database's deletion protection...");
                var keep = state.Parameters.Keys.ToDictionary(
                    k => k,
                    k => k == AwsStack.ProtectParameter ? "false" : (string?)null,
                    StringComparer.Ordinal
                );
                await aws.DeployAsync(name, null, keep, state.Tags, say, ct);
            }
            say($"Deleting {name}...");
            await aws.DeleteStackAsync(name, say, ct);
            snapshot |= state.Parameters.ContainsKey(AwsStack.ProtectParameter);
        }
        if (snapshot)
            say(
                "The database left a final snapshot, which RDS keeps until you delete it (RDS console, "
                    + "Snapshots)."
            );
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
            await aws.DeleteRepositoryAsync(Repository(stack, name), ct);
        foreach (var name in file.Secrets)
            await aws.DeleteSecretAsync(AwsStack.AppSecretName(stack, name), ct);
        say(
            "Deleted the images and the app's secrets. For seven days 'trax deploy secret set' restores a secret "
                + "with its new value."
        );
    }

    /// <summary>
    /// Refuses to delete a foundation whose network holds an interface trax did not create: one wearing a group
    /// neither stack created (an EKS node given the database client group, say), or with no group at all, other than
    /// the NAT gateway's. In a VPC the foundation created, every interface counts; in an existing VPC, only those
    /// wearing one of the foundation's groups, since the rest of that network is not the foundation's to delete.
    /// </summary>
    private async Task CheckNetworkAsync(
        string baseStack,
        AwsStackState foundation,
        AwsStackState? runtime,
        CancellationToken ct
    )
    {
        if (foundation.Outputs.GetValueOrDefault("VpcId") is not { Length: > 0 } vpc)
            return;
        var ownsVpc = foundation.Outputs.GetValueOrDefault("OwnsVpc") == "true";
        var mine = Split(foundation.Outputs.GetValueOrDefault("SecurityGroups"));
        var ours = mine.Concat(Split(runtime?.Outputs.GetValueOrDefault("SecurityGroups")))
            .ToHashSet(StringComparer.Ordinal);
        var foreign = (await aws.NetworkInterfacesAsync(vpc, ct))
            .Where(e => ownsVpc || e.SecurityGroups.Any(mine.Contains))
            .Where(e =>
                !(ownsVpc && e.Type == "nat_gateway")
                && (e.SecurityGroups.Count == 0 || !e.SecurityGroups.All(ours.Contains))
            )
            .ToList();
        if (foreign.Count == 0)
            return;
        throw new InvalidOperationException(
            $"{baseStack} cannot be deleted: its network holds what trax did not create, and deleting the "
                + "network would fail partway or strand it. Remove these first (or the cluster they belong to), "
                + "then destroy again: "
                + string.Join(", ", foreign.Select(e => $"{e.Id} ({e.Description})"))
                + ". Nothing was deleted."
        );
    }

    private static string[] Split(string? list) =>
        string.IsNullOrEmpty(list) ? [] : list.Split(',', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// What pass 1 sends for <paramref name="service"/>: its count (null keeps the stack's), and whether it stays on
    /// the image it runs. A service the stack holds no image for, or runs none of, waits at zero on the new image.
    /// </summary>
    private static (string? Count, bool Current) Holding(AwsStackState? existing, string service)
    {
        if (existing?.Parameters.ContainsKey(AwsStack.ImageParameter(service)) is not true)
            return ("0", false);
        return existing.Parameters.GetValueOrDefault(AwsStack.CountParameter(service)) == "0"
            ? ("0", false)
            : (null, true);
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

    /// <summary><see cref="Tags"/>, and which of the environment's two stacks this is.</summary>
    public static IReadOnlyDictionary<string, string> StackTags(
        DeployFile file,
        string environment,
        string part
    ) =>
        new Dictionary<string, string>(Tags(file, environment), StringComparer.Ordinal)
        {
            [PartTag] = part,
        };

    /// <summary>
    /// A tag no earlier deploy used: the time, then the commit and whether the tree had changes, so an image says
    /// what it was built from.
    /// </summary>
    public static string ImageTag(string directory, DateTime now)
    {
        var tag = now.ToString(
            "yyyyMMdd-HHmmss",
            System.Globalization.CultureInfo.InvariantCulture
        );
        var commit = Git(directory, "rev-parse", "--short=12", "HEAD");
        if (commit is null)
            return tag;
        var dirty = Git(directory, "status", "--porcelain") is { Length: > 0 };
        return $"{tag}-{commit}{(dirty ? "-dirty" : "")}";
    }

    private static string? Git(string directory, params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = directory };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        try
        {
            var (exit, stdout, _) = ChildProcess.Run(psi);
            return exit == 0 ? stdout.Trim() : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// Builds a service's image with <c>dotnet publish -t:PublishContainer</c> for linux-x64 and pushes it straight to
/// the registry, with the registry login passed in the environment: no docker or podman needed.
/// </summary>
internal sealed class DotnetImagePublisher : IImagePublisher
{
    public void Publish(string projectPath, string repositoryUri, string tag, RegistryLogin login)
    {
        var (exit, stdout, stderr) = ChildProcess.Run(
            StartInfo(projectPath, repositoryUri, tag, login)
        );
        if (exit != 0)
            throw new InvalidOperationException(
                $"Publishing {Path.GetFileName(projectPath)} to {repositoryUri} failed (exit {exit}):\n{stdout}{stderr}"
            );
    }

    internal static ProcessStartInfo StartInfo(
        string projectPath,
        string repositoryUri,
        string tag,
        RegistryLogin login
    )
    {
        var slash = repositoryUri.IndexOf('/');
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
        };
        foreach (
            var arg in new[]
            {
                "publish",
                projectPath,
                "-c",
                "Release",
                "--os",
                "linux",
                "--arch",
                "x64",
                "-t:PublishContainer",
                $"-p:ContainerRegistry={repositoryUri[..slash]}",
                $"-p:ContainerRepository={repositoryUri[(slash + 1)..]}",
                $"-p:ContainerImageTags={tag}",
            }
        )
            psi.ArgumentList.Add(arg);
        psi.Environment["DOTNET_CONTAINER_REGISTRY_UNAME"] = login.User;
        psi.Environment["DOTNET_CONTAINER_REGISTRY_PWORD"] = login.Password;
        return psi;
    }
}
