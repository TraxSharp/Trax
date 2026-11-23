using System.CommandLine;
using System.Globalization;
using Trax.Cli.Deploy;
using Trax.Cli.Deploy.Aws;
using Trax.Cli.Deploy.Kubernetes;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Commands;

/// <summary>
/// The AWS half of <c>trax deploy</c>: deploying an environment, and <c>plan</c>, <c>status</c>, <c>logs</c>,
/// <c>destroy</c> and <c>secret</c>. Every action names its environment with <c>--env</c>; the environment's region
/// picks the account's endpoints, and the AWS credential chain picks the account. <c>plan</c>, <c>status</c>,
/// <c>logs</c> and <c>destroy</c> on a kubernetes environment go to <see cref="DeployKubernetesCommands"/>.
/// </summary>
internal static class DeployAwsCommands
{
    /// <summary>The clock image tags and log windows are read from; tests swap in a fixed one.</summary>
    internal static TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>How the commands reach AWS; tests swap in a fake.</summary>
    internal static Func<string, IAwsDeployClient> Client { get; set; } =
        region => new AwsDeployClient(region, Clock);

    internal static IImagePublisher Images { get; set; } = new DotnetImagePublisher();

    internal static Func<string?> ReadLine { get; set; } = Console.ReadLine;

    public static Option<string> EnvOption(bool required) =>
        new("--env")
        {
            Description = "The environment in trax.deploy.json to act on (production, staging...).",
            Required = required,
        };

    public static Option<FileInfo?> FileOption() =>
        new("--file")
        {
            Description =
                $"The deployment's {DeployFile.FileName} (default: the one in this directory).",
        };

    private static string PathOf(ParseResult result, Option<FileInfo?> file) =>
        result.GetValue(file)?.FullName
        ?? Path.Combine(Directory.GetCurrentDirectory(), DeployFile.FileName);

    public static IEnumerable<Command> Create()
    {
        yield return Plan();
        yield return Status();
        yield return Logs();
        yield return Destroy();
        yield return Secret();
    }

    private static Command Plan()
    {
        var env = EnvOption(required: true);
        var file = FileOption();
        var command = new Command(
            "plan",
            "Write the environment's two CloudFormation templates, the foundation and the runtime (aws), or its "
                + "manifests (kubernetes), under .trax/deploy/ and say what they create."
        )
        {
            env,
            file,
        };
        command.SetAction(
            (r, ct) =>
                PlanAsync(
                    PathOf(r, file),
                    r.GetRequiredValue(env),
                    new DotnetHostExporter(ct),
                    r.InvocationConfiguration.Output,
                    r.InvocationConfiguration.Error,
                    ct
                )
        );
        return command;
    }

    private static Command Status()
    {
        var env = EnvOption(required: true);
        var file = FileOption();
        var command = new Command(
            "status",
            "Show the environment's two stacks (aws) or its namespace (kubernetes), its address and each service."
        )
        {
            env,
            file,
        };
        command.SetAction(
            (r, ct) =>
                StatusAsync(
                    PathOf(r, file),
                    r.GetRequiredValue(env),
                    r.InvocationConfiguration.Output,
                    r.InvocationConfiguration.Error,
                    ct
                )
        );
        return command;
    }

    private static Command Logs()
    {
        var service = new Argument<string>("service")
        {
            Description = "The service whose log to read, or migrate.",
        };
        var env = EnvOption(required: true);
        var file = FileOption();
        var follow = new Option<bool>("--follow") { Description = "Keep printing new lines." };
        var since = new Option<int>("--since")
        {
            Description = "How many minutes back to start (default 60).",
            DefaultValueFactory = _ => 60,
        };
        var command = new Command(
            "logs",
            "Print a service's log, from CloudWatch (aws) or its pods (kubernetes)."
        )
        {
            service,
            env,
            file,
            follow,
            since,
        };
        command.SetAction(
            (r, ct) =>
                LogsAsync(
                    PathOf(r, file),
                    r.GetRequiredValue(env),
                    r.GetRequiredValue(service),
                    r.GetValue(follow),
                    r.GetValue(since),
                    r.InvocationConfiguration.Output,
                    r.InvocationConfiguration.Error,
                    ct
                )
        );
        return command;
    }

    private static Command Destroy()
    {
        var env = EnvOption(required: true);
        var file = FileOption();
        var deleteData = new Option<bool>("--delete-data")
        {
            Description =
                "aws: also delete production's user pool with every user in it, the images and the app's secrets; "
                + "production's database still leaves a final snapshot. kubernetes: also delete the in-cluster "
                + "database's volume.",
        };
        var yes = new Option<bool>("--yes")
        {
            Description = "Do not ask for the stack's or namespace's name to be typed back.",
        };
        var command = new Command(
            "destroy",
            "Delete the environment's two stacks, the runtime and then the foundation (aws), or the app's objects "
                + "in its namespace (kubernetes)."
        )
        {
            env,
            file,
            deleteData,
            yes,
        };
        command.SetAction(
            (r, ct) =>
                DestroyAsync(
                    PathOf(r, file),
                    r.GetRequiredValue(env),
                    r.GetValue(deleteData),
                    r.GetValue(yes),
                    r.InvocationConfiguration.Output,
                    r.InvocationConfiguration.Error,
                    ct
                )
        );
        return command;
    }

    private static Command Secret()
    {
        var name = new Argument<string>("name")
        {
            Description = "A name the file lists under secrets (Stripe__ApiKey).",
        };
        var env = EnvOption(required: true);
        var file = FileOption();
        var set = new Command(
            "set",
            "Store a secret for the environment in Secrets Manager. The value is read from standard input, or "
                + "asked for without echoing it."
        )
        {
            name,
            env,
            file,
        };
        set.SetAction(
            (r, ct) =>
                SecretSetAsync(
                    PathOf(r, file),
                    r.GetRequiredValue(env),
                    r.GetRequiredValue(name),
                    () => ReadSecretAsync(r.InvocationConfiguration.Output),
                    r.InvocationConfiguration.Output,
                    r.InvocationConfiguration.Error,
                    ct
                )
        );
        var listEnv = EnvOption(required: true);
        var listFile = FileOption();
        var list = new Command(
            "list",
            "List the environment's secrets and when each last changed; never values."
        )
        {
            listEnv,
            listFile,
        };
        list.SetAction(
            (r, ct) =>
                SecretListAsync(
                    PathOf(r, listFile),
                    r.GetRequiredValue(listEnv),
                    r.InvocationConfiguration.Output,
                    r.InvocationConfiguration.Error,
                    ct
                )
        );
        return new Command("secret", "Set and list the secrets the app reads.") { set, list };
    }

    // ---- Actions ---------------------------------------------------------------------------------------

    internal static async Task<int> DeployAsync(
        string filePath,
        string environment,
        IHostExporter exporter,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct
    ) =>
        await GuardAsync(
                stderr,
                async () =>
                {
                    var file = await DeployFile.ReadAsync(filePath).ConfigureAwait(false);
                    var env = Aws(file, environment, "deploy");
                    var exports = await DeployCommand
                        .ExportAsync(file, filePath, exporter, stdout)
                        .ConfigureAwait(false);
                    var deployer = new AwsDeployer(
                        Client(env.Region ?? ""),
                        Images,
                        stdout.WriteLine
                    );
                    var tag = await ImageTags
                        .CreateAsync(
                            Path.GetDirectoryName(Path.GetFullPath(filePath)).NotNull(),
                            Clock.GetUtcNow().UtcDateTime
                        )
                        .ConfigureAwait(false);
                    var url = await deployer
                        .DeployAsync(file, filePath, environment, exports, tag, ct)
                        .ConfigureAwait(false);
                    await stdout
                        .WriteLineAsync(
                            url is null
                                ? $"Deployed {file.StackName(environment)}."
                                : $"Deployed: {url}"
                        )
                        .ConfigureAwait(false);
                }
            )
            .ConfigureAwait(false);

    internal static async Task<int> PlanAsync(
        string filePath,
        string environment,
        IHostExporter exporter,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct
    ) =>
        await IsKubernetesAsync(filePath, environment).ConfigureAwait(false)
            ? await DeployKubernetesCommands
                .PlanAsync(filePath, environment, exporter, stdout, stderr, ct)
                .ConfigureAwait(false)
            : await AwsPlanAsync(filePath, environment, exporter, stdout, stderr, ct)
                .ConfigureAwait(false);

    private static async Task<int> AwsPlanAsync(
        string filePath,
        string environment,
        IHostExporter exporter,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct
    ) =>
        await GuardAsync(
                stderr,
                async () =>
                {
                    var file = await DeployFile.ReadAsync(filePath).ConfigureAwait(false);
                    var env = Aws(file, environment, "plan");
                    var exports = await DeployCommand
                        .ExportAsync(file, filePath, exporter, stdout)
                        .ConfigureAwait(false);
                    var templates = await new AwsDeployer(
                        Client(env.Region ?? ""),
                        Images,
                        stdout.WriteLine
                    )
                        .PlanAsync(file, environment, exports, ct)
                        .ConfigureAwait(false);
                    var dir = Path.Combine(
                        Path.GetDirectoryName(Path.GetFullPath(filePath)).NotNull(),
                        ".trax",
                        "deploy",
                        "aws"
                    );
                    Directory.CreateDirectory(dir);
                    foreach (
                        var (stack, template) in new[]
                        {
                            (file.FoundationStackName(environment), templates.Foundation),
                            (file.StackName(environment), templates.Runtime),
                        }
                    )
                    {
                        var path = Path.Combine(dir, $"{stack}.template.json");
                        await File.WriteAllTextAsync(path, AwsStack.Indented(template), ct)
                            .ConfigureAwait(false);
                        await stdout.WriteLineAsync($"Wrote {path}").ConfigureAwait(false);
                    }
                    foreach (var line in AwsPlanSummary.Lines(file, environment, templates))
                        await stdout.WriteLineAsync(line).ConfigureAwait(false);
                }
            )
            .ConfigureAwait(false);

    internal static async Task<int> StatusAsync(
        string filePath,
        string environment,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct
    ) =>
        await IsKubernetesAsync(filePath, environment).ConfigureAwait(false)
            ? await DeployKubernetesCommands
                .StatusAsync(filePath, environment, stdout, stderr, ct)
                .ConfigureAwait(false)
            : await AwsStatusAsync(filePath, environment, stdout, stderr, ct).ConfigureAwait(false);

    private static async Task<int> AwsStatusAsync(
        string filePath,
        string environment,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct
    ) =>
        await GuardAsync(
                stderr,
                async () =>
                {
                    var file = await DeployFile.ReadAsync(filePath).ConfigureAwait(false);
                    var env = Aws(file, environment, "status");
                    var aws = Client(env.Region ?? "");
                    var stack = file.StackName(environment);
                    await FoundationStatusAsync(
                            aws,
                            file.FoundationStackName(environment),
                            stdout,
                            ct
                        )
                        .ConfigureAwait(false);
                    var state = await aws.DescribeStackAsync(stack, ct).ConfigureAwait(false);
                    if (state is null)
                    {
                        await stdout
                            .WriteLineAsync($"{stack} is not deployed.")
                            .ConfigureAwait(false);
                        return;
                    }
                    await stdout.WriteLineAsync($"{stack}: {state.Status}").ConfigureAwait(false);
                    if (state.Outputs.TryGetValue("Url", out var url))
                        await stdout.WriteLineAsync($"  {url}").ConfigureAwait(false);
                    if (!state.Outputs.TryGetValue("ClusterName", out var cluster))
                        return;
                    var names = file
                        .Services.Keys.Select(s =>
                            state.Outputs.GetValueOrDefault(AwsStack.ServiceOutput(s))
                        )
                        .OfType<string>()
                        .ToList();
                    foreach (
                        var service in await aws.DescribeServicesAsync(cluster, names, ct)
                            .ConfigureAwait(false)
                    )
                        await stdout
                            .WriteLineAsync(
                                string.Create(
                                    CultureInfo.InvariantCulture,
                                    $"  {service.Name, -16}{service.Running}/{service.Desired} running"
                                )
                                    + (
                                        service.Pending > 0
                                            ? string.Create(
                                                CultureInfo.InvariantCulture,
                                                $", {service.Pending} starting"
                                            )
                                            : ""
                                    )
                                    + $"  {service.Rollout, -12}{service.TaskDefinition}"
                            )
                            .ConfigureAwait(false);
                }
            )
            .ConfigureAwait(false);

    // The foundation's status, when it exists: the runtime's line says when nothing is deployed.
    private static async Task FoundationStatusAsync(
        IAwsDeployClient aws,
        string stack,
        TextWriter stdout,
        CancellationToken ct
    )
    {
        var foundation = await aws.DescribeStackAsync(stack, ct).ConfigureAwait(false);
        if (foundation is not null)
            await stdout.WriteLineAsync($"{stack}: {foundation.Status}").ConfigureAwait(false);
    }

    internal static async Task<int> LogsAsync(
        string filePath,
        string environment,
        string service,
        bool follow,
        int sinceMinutes,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct
    ) =>
        await IsKubernetesAsync(filePath, environment).ConfigureAwait(false)
            ? await DeployKubernetesCommands
                .LogsAsync(filePath, environment, service, follow, sinceMinutes, stdout, stderr, ct)
                .ConfigureAwait(false)
            : await AwsLogsAsync(
                    filePath,
                    environment,
                    service,
                    follow,
                    sinceMinutes,
                    stdout,
                    stderr,
                    ct
                )
                .ConfigureAwait(false);

    private static async Task<int> AwsLogsAsync(
        string filePath,
        string environment,
        string service,
        bool follow,
        int sinceMinutes,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct
    ) =>
        await GuardAsync(
                stderr,
                async () =>
                {
                    var file = await DeployFile.ReadAsync(filePath).ConfigureAwait(false);
                    var env = Aws(file, environment, "logs");
                    if (
                        !string.Equals(service, AwsStack.MigrateContainer, StringComparison.Ordinal)
                        && !file.Services.ContainsKey(service)
                    )
                        throw new InvalidOperationException(
                            $"'{service}' is not a service. Read one of: {string.Join(", ", file.Services.Keys.Append(AwsStack.MigrateContainer))}."
                        );
                    var group = AwsStack.LogGroup(file.StackName(environment), service);
                    await foreach (
                        var line in Client(env.Region ?? "")
                            .LogsAsync(
                                group,
                                Clock.GetUtcNow().UtcDateTime.AddMinutes(-sinceMinutes),
                                follow,
                                ct
                            )
                            .ConfigureAwait(false)
                    )
                        await stdout.WriteLineAsync(line).ConfigureAwait(false);
                }
            )
            .ConfigureAwait(false);

    /// <summary>What destroy asks before it deletes anything: what goes, and what happens to the users.</summary>
    internal static string DestroyPrompt(DeployFile file, string environment, bool deleteData)
    {
        var env = file.Environment(environment);
        var stack = file.StackName(environment);
        var cognito = env.Auth is { Kind: DeployAuth.Cognito };
        var production = string.Equals(
            env.Profile,
            DeployEnvironment.Production,
            StringComparison.Ordinal
        );
        var gone = new List<string> { $"{stack} and {file.FoundationStackName(environment)}" };
        if (cognito && (deleteData || !production))
            gone.Add("its user pool with every user in it");
        if (deleteData)
            gone.Add("its images and its secrets");
        var users =
            cognito && production && !deleteData
                ? " Its user pool and every user in it are kept, and the next deploy signs in against them; "
                    + "--delete-data deletes them."
                : "";
        return $"This deletes {string.Join(", ", gone)}.{users} Type {stack} to go on: ";
    }

    internal static async Task<int> DestroyAsync(
        string filePath,
        string environment,
        bool deleteData,
        bool yes,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct
    ) =>
        await IsKubernetesAsync(filePath, environment).ConfigureAwait(false)
            ? await DeployKubernetesCommands
                .DestroyAsync(filePath, environment, deleteData, yes, ReadLine, stdout, stderr, ct)
                .ConfigureAwait(false)
            : await AwsDestroyAsync(filePath, environment, deleteData, yes, stdout, stderr, ct)
                .ConfigureAwait(false);

    private static async Task<int> AwsDestroyAsync(
        string filePath,
        string environment,
        bool deleteData,
        bool yes,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct
    ) =>
        await GuardAsync(
                stderr,
                async () =>
                {
                    var file = await DeployFile.ReadAsync(filePath).ConfigureAwait(false);
                    var env = Aws(file, environment, "destroy");
                    var stack = file.StackName(environment);
                    if (!yes)
                    {
                        await stdout
                            .WriteAsync(DestroyPrompt(file, environment, deleteData))
                            .ConfigureAwait(false);
                        if (!string.Equals(ReadLine()?.Trim(), stack, StringComparison.Ordinal))
                            throw new InvalidOperationException("Nothing was deleted.");
                    }
                    await new AwsDeployer(Client(env.Region ?? ""), Images, stdout.WriteLine)
                        .DestroyAsync(file, environment, deleteData, ct)
                        .ConfigureAwait(false);
                }
            )
            .ConfigureAwait(false);

    internal static async Task<int> SecretSetAsync(
        string filePath,
        string environment,
        string name,
        Func<Task<string>> readValue,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct
    ) =>
        await GuardAsync(
                stderr,
                async () =>
                {
                    var file = await DeployFile.ReadAsync(filePath).ConfigureAwait(false);
                    var env = Aws(file, environment, "secret set");
                    if (!file.Secrets.Contains(name))
                        throw new InvalidOperationException(
                            $"'{name}' is not listed under secrets in {DeployFile.FileName}. Add it there first, so every "
                                + "deploy knows to supply it."
                        );
                    var value = await readValue().ConfigureAwait(false);
                    if (value.Length == 0)
                        throw new InvalidOperationException(
                            "The value is empty; nothing was stored."
                        );
                    var stack = file.StackName(environment);
                    await Client(env.Region ?? "")
                        .PutSecretAsync(
                            AwsStack.AppSecretName(stack, name),
                            value,
                            AwsDeployer.Tags(file, environment),
                            ct
                        )
                        .ConfigureAwait(false);
                    await stdout
                        .WriteLineAsync(
                            $"Stored {AwsStack.AppSecretName(stack, name)}. Deploy again for the services to read it."
                        )
                        .ConfigureAwait(false);
                }
            )
            .ConfigureAwait(false);

    internal static async Task<int> SecretListAsync(
        string filePath,
        string environment,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken ct
    ) =>
        await GuardAsync(
                stderr,
                async () =>
                {
                    var file = await DeployFile.ReadAsync(filePath).ConfigureAwait(false);
                    var env = Aws(file, environment, "secret list");
                    var stack = file.StackName(environment);
                    var stored = (
                        await Client(env.Region ?? "")
                            .ListSecretsAsync(AwsStack.AppSecretName(stack, ""), ct)
                            .ConfigureAwait(false)
                    ).ToDictionary(s => s.Name, s => s.Changed, StringComparer.Ordinal);
                    foreach (var name in file.Secrets)
                        await stdout
                            .WriteLineAsync(
                                stored.TryGetValue(
                                    AwsStack.AppSecretName(stack, name),
                                    out var changed
                                )
                                    ? string.Create(
                                        CultureInfo.InvariantCulture,
                                        $"  {name, -32}set {changed:yyyy-MM-dd HH:mm}"
                                    )
                                    : $"  {name, -32}NOT SET"
                            )
                            .ConfigureAwait(false);
                }
            )
            .ConfigureAwait(false);

    /// <summary>
    /// Whether the environment's target is kubernetes. False when the file cannot say; the aws action then reports
    /// why.
    /// </summary>
    internal static async Task<bool> IsKubernetesAsync(string filePath, string environment)
    {
        try
        {
            var file = await DeployFile.ReadAsync(filePath).ConfigureAwait(false);
            return string.Equals(
                file.Environments.GetValueOrDefault(environment)?.Target,
                DeployEnvironment.Kubernetes,
                StringComparison.Ordinal
            );
        }
        catch (Exception ex) when (DeployCommand.Reported(ex))
        {
            return false;
        }
    }

    /// <summary>The environment, checked, refused unless its target is aws.</summary>
    private static DeployEnvironment Aws(DeployFile file, string environment, string command)
    {
        var env = file.Environment(environment);
        return env.Target switch
        {
            DeployEnvironment.Aws => env,
            DeployEnvironment.Kubernetes => throw new InvalidOperationException(
                $"'trax deploy {command}' is for aws environments, and '{environment}' is a kubernetes one. Its "
                    + $"secrets live in the Secret {file.StackName(environment)} in its namespace. Write them into a "
                    + "file, one KEY=value line each, and set them with 'kubectl create secret generic "
                    + $"{file.StackName(environment)} -n {KubernetesTarget.NamespaceName(file, environment, env)} "
                    + "--from-env-file=<file> --dry-run=client -o yaml | kubectl apply -f -', then delete the file. "
                    + "'kubectl describe secret' lists them."
            ),
            _ => throw new InvalidOperationException(
                $"'trax deploy {command}' is for aws and kubernetes environments, and '{environment}' is a "
                    + $"{env.Target} one. 'trax deploy --env {environment}' writes its stack."
            ),
        };
    }

    /// <summary>The value from standard input when it is redirected, else typed without echo.</summary>
    private static async Task<string> ReadSecretAsync(TextWriter stdout)
    {
        if (Console.IsInputRedirected)
            return (await Console.In.ReadToEndAsync().ConfigureAwait(false)).TrimEnd('\r', '\n');
        await stdout.WriteAsync("Value (not shown): ").ConfigureAwait(false);
        var value = ReadHidden(() => Console.ReadKey(intercept: true));
        await stdout.WriteLineAsync().ConfigureAwait(false);
        return value;
    }

    /// <summary>The characters typed until Enter, with Backspace taking one back and every other key ignored.</summary>
    internal static string ReadHidden(Func<ConsoleKeyInfo> readKey)
    {
        var value = new System.Text.StringBuilder();
        while (true)
        {
            var key = readKey();
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length > 0)
                    value.Length--;
                continue;
            }
            // Arrows, Home, End, Delete and Tab are control characters ('\0', '\t'), which nobody sees typed with
            // echo off and which would end up in the stored value.
            if (char.IsControl(key.KeyChar))
                continue;
            value.Append(key.KeyChar);
        }
        return value.ToString();
    }

    private static async Task<int> GuardAsync(TextWriter stderr, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
            when (DeployCommand.Reported(ex)
                || ex
                    is Amazon.Runtime.AmazonServiceException
                        or Amazon.Runtime.AmazonClientException
            )
        {
            await stderr.WriteLineAsync(ex.Message).ConfigureAwait(false);
            return 1;
        }
    }
}
