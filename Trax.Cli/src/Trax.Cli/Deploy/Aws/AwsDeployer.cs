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
        var tags = Tags(file, environment);

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

        var existing = await aws.DescribeStackAsync(stack, ct);
        if (existing is { Dead: true })
            throw new InvalidOperationException(
                $"{stack} is {existing.Status}: its creation failed and it can only be deleted. Run "
                    + $"'trax deploy destroy --env {environment}', then deploy again."
            );
        if (existing is { Settled: false })
            throw new InvalidOperationException(
                $"{stack} is {existing.Status}. Wait for it to settle, then deploy again."
            );

        var template = AwsStack.Compact(await PlanAsync(file, environment, exports, ct));
        if (template.Length > InlineTemplateLimit)
            throw new InvalidOperationException(
                $"The template is {template.Length} bytes, over CloudFormation's {InlineTemplateLimit}-byte limit "
                    + "for a template sent inline."
            );

        say($"Pushing images tagged {tag}...");
        var login = await aws.RegistryLoginAsync(ct);
        var image = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, service) in file.Services)
        {
            var repository = await aws.EnsureRepositoryAsync(Repository(stack, name), tags, ct);
            say($"  {name} -> {repository}:{tag}");
            images.Publish(DeployFile.ProjectPath(filePath, service), repository, tag, login);
            image[name] = $"{repository}:{tag}";
        }
        var migrator = image[AwsStack.Migrator(file, exports)];

        // Pass 1: the infrastructure, and the migrate task on the new image. A new stack starts its services at
        // zero; an existing one keeps them on the image they run.
        var waiting =
            existing is null
            || existing.Parameters.GetValueOrDefault(AwsStack.ScaleParameter) == "0";
        var first = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [AwsStack.ScaleParameter] = waiting ? "0" : "1",
            [AwsStack.MigrateImageParameter] = migrator,
        };
        foreach (var name in file.Services.Keys)
        {
            var key = AwsStack.ImageParameter(name);
            first[key] =
                waiting || existing?.Parameters.ContainsKey(key) is not true ? image[name] : null;
        }
        if (existing?.Parameters.ContainsKey(AwsStack.ProtectParameter) is true)
            first[AwsStack.ProtectParameter] = "true";
        say(existing is null ? $"Creating {stack}..." : $"Updating {stack}...");
        await aws.DeployAsync(stack, template, first, tags, say, ct);

        var outputs = (await aws.DescribeStackAsync(stack, ct))!.Outputs;
        say("Migrating the database...");
        var migration = await aws.RunTaskAsync(
            new AwsTaskRequest(
                outputs["ClusterName"],
                outputs["MigrateTaskDefinition"],
                AwsStack.MigrateContainer,
                outputs["ServiceSubnets"].Split(','),
                outputs["TaskSecurityGroup"],
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

        // Pass 2: every service onto the new image, rolled with the circuit breaker on.
        var second = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [AwsStack.ScaleParameter] = "1",
            [AwsStack.MigrateImageParameter] = migrator,
        };
        foreach (var name in file.Services.Keys)
            second[AwsStack.ImageParameter(name)] = image[name];
        if (first.ContainsKey(AwsStack.ProtectParameter))
            second[AwsStack.ProtectParameter] = "true";
        say($"Rolling the services onto {tag}...");
        await aws.DeployAsync(stack, template, second, tags, say, ct);

        return (await aws.DescribeStackAsync(stack, ct))!.Outputs.GetValueOrDefault("Url");
    }

    /// <summary>
    /// Deletes the environment's stack. Production's database is unprotected first and keeps a final snapshot;
    /// with <paramref name="deleteData"/> the images and the app's secrets go too.
    /// </summary>
    public async Task DestroyAsync(
        DeployFile file,
        string environment,
        bool deleteData,
        CancellationToken ct
    )
    {
        file.Environment(environment);
        var stack = file.StackName(environment);
        var existing = await aws.DescribeStackAsync(stack, ct);
        if (existing is null)
            say($"{stack} does not exist.");
        else
        {
            if (existing.Parameters.ContainsKey(AwsStack.ProtectParameter) && !existing.Dead)
            {
                say("Turning off the database's deletion protection...");
                var keep = existing.Parameters.Keys.ToDictionary(
                    k => k,
                    k => k == AwsStack.ProtectParameter ? "false" : (string?)null,
                    StringComparer.Ordinal
                );
                await aws.DeployAsync(stack, null, keep, Tags(file, environment), say, ct);
            }
            say($"Deleting {stack}...");
            await aws.DeleteStackAsync(stack, say, ct);
            if (existing.Parameters.ContainsKey(AwsStack.ProtectParameter))
                say(
                    "The database left a final snapshot, which RDS keeps until you delete it (RDS console, "
                        + "Snapshots)."
                );
        }
        if (!deleteData)
            return;
        foreach (var name in file.Services.Keys)
            await aws.DeleteRepositoryAsync(Repository(stack, name), ct);
        foreach (var name in file.Secrets)
            await aws.DeleteSecretAsync(AwsStack.AppSecretName(stack, name), ct);
        say("Deleted the images and the app's secrets (a secret can be restored for seven days).");
    }

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
