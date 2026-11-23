using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Deploy.Aws;

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
        var baseStack = file.FoundationStackName(environment);
        var foundation = await ExistingAsync(baseStack, file, FoundationPart, ct)
            .ConfigureAwait(false);
        var existing = await ExistingAsync(stack, file, RuntimePart, ct).ConfigureAwait(false);

        await CheckSecretsAsync(file, stack, environment, ct).ConfigureAwait(false);

        CheckSettled(foundation, baseStack, environment);
        CheckSettled(existing, stack, environment);

        var templates = await PlanAsync(file, environment, exports, ct).ConfigureAwait(false);
        var foundationTemplate = Inline(baseStack, templates.Foundation);
        var runtimeTemplate = Inline(stack, templates.Runtime);

        var image = await PushImagesAsync(file, filePath, stack, tag, Tags(file, environment), ct)
            .ConfigureAwait(false);
        var migrator = image[AwsStack.Migrator(file, exports)];

        var given = await DeployFoundationAsync(
                file,
                environment,
                foundation,
                foundationTemplate,
                templates.Runtime,
                ct
            )
            .ConfigureAwait(false);

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
        await aws.DeployAsync(stack, runtimeTemplate, first, tags, say, ct).ConfigureAwait(false);

        await MigrateAsync(stack, environment, ct).ConfigureAwait(false);

        var second = SecondPassParameters(file, env, migrator, image, given);
        say($"Rolling the services onto {tag}...");
        await aws.DeployAsync(stack, runtimeTemplate, second, tags, say, ct).ConfigureAwait(false);

        return (await aws.DescribeStackAsync(stack, ct).ConfigureAwait(false))
            .NotNull()
            .Outputs.GetValueOrDefault("Url");
    }

    // The foundation first: the runtime reads what it outputs. Returns the outputs the runtime takes as parameters.
    private async Task<Dictionary<string, string?>> DeployFoundationAsync(
        DeployFile file,
        string environment,
        AwsStackState? foundation,
        string template,
        JsonObject runtime,
        CancellationToken ct
    )
    {
        var baseStack = file.FoundationStackName(environment);
        var under = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (foundation?.Parameters.ContainsKey(AwsStack.ProtectParameter) is true)
            under[AwsStack.ProtectParameter] = "true";
        if (KeepsItsUserPool(file.Environment(environment)))
            under[AwsStack.UserPoolParameter] = await UserPoolAsync(
                    file.StackName(environment),
                    foundation,
                    ct
                )
                .ConfigureAwait(false);
        say(foundation is null ? $"Creating {baseStack}..." : $"Updating {baseStack}...");
        await aws.DeployAsync(
                baseStack,
                template,
                under,
                StackTags(file, environment, FoundationPart),
                say,
                ct
            )
            .ConfigureAwait(false);
        var outputs = (await aws.DescribeStackAsync(baseStack, ct).ConfigureAwait(false))
            .NotNull()
            .Outputs;
        return FoundationValues(outputs, runtime);
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
        var state = await aws.DescribeStackAsync(stack, ct).ConfigureAwait(false);
        if (state is null or { Created: false })
            return null;
        CheckOwner(state, file, stack);
        var found = state.Tags.GetValueOrDefault(PartTag);
        if (string.Equals(found, part, StringComparison.Ordinal))
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
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The template of {stack} is {text.Length} bytes, over CloudFormation's {InlineTemplateLimit}-byte "
                ) + "limit for a template sent inline."
            );
        return text;
    }

    /// <summary>The foundation's outputs the runtime template declares as parameters.</summary>
    private static Dictionary<string, string?> FoundationValues(
        IReadOnlyDictionary<string, string> outputs,
        JsonObject runtime
    )
    {
        var declared = (JsonObject)runtime["Parameters"].NotNull();
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var name in AwsStack.FoundationValues.Where(declared.ContainsKey))
            values[name] = outputs.TryGetValue(name, out var value)
                ? value
                : throw new InvalidOperationException(
                    $"The foundation stack has no output {name}, which the services need. Deploy again."
                );
        return values;
    }

    // Nothing is created until every secret the app needs has a value.
    private async Task CheckSecretsAsync(
        DeployFile file,
        string stack,
        string environment,
        CancellationToken ct
    )
    {
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
    }

    private static void CheckSettled(AwsStackState? existing, string stack, string environment)
    {
        if (existing is { Dead: true })
            throw new InvalidOperationException(
                $"{stack} is {existing.Status}: its creation failed and it can only be deleted. Run "
                    + $"'trax deploy destroy --env {environment}', then deploy again."
            );
        if (existing is { Settled: false })
            throw new InvalidOperationException(
                $"{stack} is {existing.Status}. Wait for it to settle, then deploy again."
            );
    }

    // Pushes each service's image and returns where each went, by service.
    private async Task<Dictionary<string, string>> PushImagesAsync(
        DeployFile file,
        string filePath,
        string stack,
        string tag,
        IReadOnlyDictionary<string, string> tags,
        CancellationToken ct
    )
    {
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
        return image;
    }

    private async Task MigrateAsync(string stack, string environment, CancellationToken ct)
    {
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
                    outputs["TaskSecurityGroups"].Split(','),
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
    }

    // Pass 2: every service onto the new image at its count, rolled with the circuit breaker on.
    private static Dictionary<string, string?> SecondPassParameters(
        DeployFile file,
        DeployEnvironment env,
        string migrator,
        Dictionary<string, string> image,
        Dictionary<string, string?> given
    )
    {
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
        return second;
    }

    /// <summary>Whether the environment's user pool outlives its stack: production's Cognito pool.</summary>
    private static bool KeepsItsUserPool(DeployEnvironment env) =>
        string.Equals(env.Profile, DeployEnvironment.Production, StringComparison.Ordinal)
        && env.Auth is { Kind: DeployAuth.Cognito };

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
        var kept = await aws.GetParameterAsync(UserPoolRecord(stack), ct).ConfigureAwait(false);
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
        var runtime = await aws.DescribeStackAsync(stack, ct).ConfigureAwait(false);
        var foundation = await aws.DescribeStackAsync(baseStack, ct).ConfigureAwait(false);
        if (runtime is not null)
            CheckOwner(runtime, file, stack);
        if (foundation is not null)
            CheckOwner(foundation, file, baseStack);
        if (foundation is { Dead: false })
            await CheckNetworkAsync(baseStack, foundation, runtime, ct).ConfigureAwait(false);

        var record = UserPoolRecord(stack);
        var pool = KeepsItsUserPool(env)
            ? foundation?.Outputs.GetValueOrDefault("UserPoolId")
                // A single stack from before the split kept its pool in its own outputs.
                ?? runtime?.Outputs.GetValueOrDefault("UserPoolId")
                ?? await aws.GetParameterAsync(record, ct).ConfigureAwait(false)
            : null;
        // Recorded before the stacks go, so a destroy that stops partway still leaves the pool findable.
        if (pool is not null && !deleteData)
            await aws.PutParameterAsync(record, pool, Tags(file, environment), ct)
                .ConfigureAwait(false);
        if (runtime is null && foundation is null)
            say($"{stack} does not exist.");
        var snapshot = false;
        foreach (var (name, state) in new[] { (stack, runtime), (baseStack, foundation) })
            if (state is not null)
                snapshot |= await DeleteExistingStackAsync(state, name, ct).ConfigureAwait(false);
        if (snapshot)
            say(
                "The database left a final snapshot, which RDS keeps until you delete it (RDS console, "
                    + "Snapshots)."
            );
        if (pool is not null)
            await SettleUserPoolAsync(pool, record, deleteData, environment, ct)
                .ConfigureAwait(false);
        if (deleteData)
            await DeleteDataAsync(file, stack, ct).ConfigureAwait(false);
    }

    private async Task DeleteDataAsync(DeployFile file, string stack, CancellationToken ct)
    {
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

    // Deletes one stack, unprotecting its database first; true when the database leaves a final snapshot.
    private async Task<bool> DeleteExistingStackAsync(
        AwsStackState existing,
        string stack,
        CancellationToken ct
    )
    {
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
            await aws.DeployAsync(stack, template: null, keep, existing.Tags, say, ct)
                .ConfigureAwait(false);
        }
        say($"Deleting {stack}...");
        await aws.DeleteStackAsync(stack, say, ct).ConfigureAwait(false);
        return existing.Parameters.ContainsKey(AwsStack.ProtectParameter);
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
        var ownsVpc = string.Equals(
            foundation.Outputs.GetValueOrDefault("OwnsVpc"),
            "true",
            StringComparison.Ordinal
        );
        var mine = Split(foundation.Outputs.GetValueOrDefault("SecurityGroups"));
        var ours = mine.Concat(Split(runtime?.Outputs.GetValueOrDefault("SecurityGroups")))
            .ToHashSet(StringComparer.Ordinal);
        var foreign = (await aws.NetworkInterfacesAsync(vpc, ct).ConfigureAwait(false))
            .Where(e => ownsVpc || e.SecurityGroups.Any(mine.Contains))
            .Where(e =>
                !(ownsVpc && string.Equals(e.Type, "nat_gateway", StringComparison.Ordinal))
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

    // Production's pool, once its stack is gone: deleted with the data, else kept and recorded for the next deploy.
    private async Task SettleUserPoolAsync(
        string pool,
        string record,
        bool deleteData,
        string environment,
        CancellationToken ct
    )
    {
        if (deleteData)
        {
            await aws.DeleteUserPoolAsync(pool, ct).ConfigureAwait(false);
            await aws.DeleteParameterAsync(record, ct).ConfigureAwait(false);
            say($"Deleted the user pool {pool} and every user in it.");
        }
        else
        {
            var users = await aws.UserCountAsync(pool, ct).ConfigureAwait(false);
            say(
                users is null
                    ? $"The user pool {pool} recorded in {record} no longer exists; the next deploy creates one."
                    : $"Kept the user pool {pool} with its {users.Value.ToString(CultureInfo.InvariantCulture)} users (Cognito's estimate), recorded in "
                        + $"{record}. The next deploy of {environment} signs in against it; "
                        + $"'trax deploy destroy --env {environment} --delete-data' deletes it."
            );
            if (users is null)
                await aws.DeleteParameterAsync(record, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// What pass 1 sends for <paramref name="service"/>: its count (null keeps the stack's), and whether it stays on
    /// the image it runs. A service the stack holds no image for, or runs none of, waits at zero on the new image.
    /// </summary>
    private static (string? Count, bool Current) Holding(AwsStackState? existing, string service)
    {
        if (existing?.Parameters.ContainsKey(AwsStack.ImageParameter(service)) is not true)
            return ("0", false);
        return string.Equals(
            existing.Parameters.GetValueOrDefault(AwsStack.CountParameter(service)),
            "0",
            StringComparison.Ordinal
        )
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
        if (string.Equals(owner, file.App, StringComparison.Ordinal))
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
