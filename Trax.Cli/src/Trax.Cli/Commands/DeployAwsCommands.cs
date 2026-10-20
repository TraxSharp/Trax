using System.CommandLine;
using Trax.Cli.Deploy;
using Trax.Cli.Deploy.Aws;
using Trax.Effect.Configuration.Deploy;

namespace Trax.Cli.Commands;

/// <summary>
/// The AWS half of <c>trax deploy</c>: deploying an environment, and <c>plan</c>, <c>status</c>, <c>logs</c>,
/// <c>destroy</c> and <c>secret</c>. Every action names its environment with <c>--env</c>; the environment's region
/// picks the account's endpoints, and the AWS credential chain picks the account.
/// </summary>
internal static class DeployAwsCommands
{
    /// <summary>How the commands reach AWS; tests swap in a fake.</summary>
    internal static Func<string, IAwsDeployClient> Client { get; set; } =
        region => new AwsDeployClient(region);

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
            "Write the environment's CloudFormation template under .trax/deploy/aws/ and say what it creates."
        )
        {
            env,
            file,
        };
        command.SetAction(
            (r, ct) =>
                PlanAsync(PathOf(r, file), r.GetRequiredValue(env), new DotnetHostExporter(), ct)
        );
        return command;
    }

    private static Command Status()
    {
        var env = EnvOption(required: true);
        var file = FileOption();
        var command = new Command(
            "status",
            "Show the environment's stack, its address and each service."
        )
        {
            env,
            file,
        };
        command.SetAction((r, ct) => StatusAsync(PathOf(r, file), r.GetRequiredValue(env), ct));
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
        var command = new Command("logs", "Print a service's log from CloudWatch.")
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
                "Also delete the images and the app's secrets. Production's database still leaves a final snapshot.",
        };
        var yes = new Option<bool>("--yes")
        {
            Description = "Do not ask for the stack's name to be typed back.",
        };
        var command = new Command("destroy", "Delete the environment's stack.")
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
                    ReadSecret,
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
            (r, ct) => SecretListAsync(PathOf(r, listFile), r.GetRequiredValue(listEnv), ct)
        );
        return new Command("secret", "Set and list the secrets the app reads.") { set, list };
    }

    // ---- Actions ---------------------------------------------------------------------------------------

    internal static async Task<int> DeployAsync(
        string filePath,
        string environment,
        IHostExporter exporter,
        CancellationToken ct
    ) =>
        await Guard(async () =>
        {
            var file = DeployFile.Read(filePath);
            var env = file.Environment(environment);
            var exports = DeployCommand.Export(file, filePath, exporter);
            var deployer = new AwsDeployer(Client(env.Region), Images, Console.WriteLine);
            var tag = AwsDeployer.ImageTag(
                Path.GetDirectoryName(Path.GetFullPath(filePath))!,
                DateTime.UtcNow
            );
            var url = await deployer.DeployAsync(file, filePath, environment, exports, tag, ct);
            Console.WriteLine(
                url is null ? $"Deployed {file.StackName(environment)}." : $"Deployed: {url}"
            );
        });

    internal static async Task<int> PlanAsync(
        string filePath,
        string environment,
        IHostExporter exporter,
        CancellationToken ct
    ) =>
        await Guard(async () =>
        {
            var file = DeployFile.Read(filePath);
            var env = file.Environment(environment);
            var exports = DeployCommand.Export(file, filePath, exporter);
            var template = await new AwsDeployer(
                Client(env.Region),
                Images,
                Console.WriteLine
            ).PlanAsync(file, environment, exports, ct);
            var dir = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(filePath))!,
                ".trax",
                "deploy",
                "aws"
            );
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{file.StackName(environment)}.template.json");
            File.WriteAllText(path, AwsStack.Indented(template));
            Console.WriteLine($"Wrote {path}");
            foreach (var line in AwsPlanSummary.Lines(file, environment, template))
                Console.WriteLine(line);
        });

    internal static async Task<int> StatusAsync(
        string filePath,
        string environment,
        CancellationToken ct
    ) =>
        await Guard(async () =>
        {
            var file = DeployFile.Read(filePath);
            var env = file.Environment(environment);
            var aws = Client(env.Region);
            var stack = file.StackName(environment);
            var state = await aws.DescribeStackAsync(stack, ct);
            if (state is null)
            {
                Console.WriteLine($"{stack} is not deployed.");
                return;
            }
            Console.WriteLine($"{stack}: {state.Status}");
            if (state.Outputs.TryGetValue("Url", out var url))
                Console.WriteLine($"  {url}");
            if (!state.Outputs.TryGetValue("ClusterName", out var cluster))
                return;
            var names = file
                .Services.Keys.Select(s =>
                    state.Outputs.GetValueOrDefault(AwsStack.ServiceOutput(s))
                )
                .OfType<string>()
                .ToList();
            foreach (var service in await aws.DescribeServicesAsync(cluster, names, ct))
                Console.WriteLine(
                    $"  {service.Name, -16}{service.Running}/{service.Desired} running"
                        + (service.Pending > 0 ? $", {service.Pending} starting" : "")
                        + $"  {service.Rollout, -12}{service.TaskDefinition}"
                );
        });

    internal static async Task<int> LogsAsync(
        string filePath,
        string environment,
        string service,
        bool follow,
        int sinceMinutes,
        CancellationToken ct
    ) =>
        await Guard(async () =>
        {
            var file = DeployFile.Read(filePath);
            var env = file.Environment(environment);
            if (service != AwsStack.MigrateContainer && !file.Services.ContainsKey(service))
                throw new InvalidOperationException(
                    $"'{service}' is not a service. Read one of: {string.Join(", ", file.Services.Keys.Append(AwsStack.MigrateContainer))}."
                );
            var group = AwsStack.LogGroup(file.StackName(environment), service);
            await foreach (
                var line in Client(env.Region)
                    .LogsAsync(group, DateTime.UtcNow.AddMinutes(-sinceMinutes), follow, ct)
            )
                Console.WriteLine(line);
        });

    internal static async Task<int> DestroyAsync(
        string filePath,
        string environment,
        bool deleteData,
        bool yes,
        CancellationToken ct
    ) =>
        await Guard(async () =>
        {
            var file = DeployFile.Read(filePath);
            var env = file.Environment(environment);
            var stack = file.StackName(environment);
            if (!yes)
            {
                Console.Write(
                    $"This deletes {stack}{(deleteData ? ", its images and its secrets" : "")}. Type {stack} to go on: "
                );
                if (ReadLine()?.Trim() != stack)
                    throw new InvalidOperationException("Nothing was deleted.");
            }
            await new AwsDeployer(Client(env.Region), Images, Console.WriteLine).DestroyAsync(
                file,
                environment,
                deleteData,
                ct
            );
        });

    internal static async Task<int> SecretSetAsync(
        string filePath,
        string environment,
        string name,
        Func<string> readValue,
        CancellationToken ct
    ) =>
        await Guard(async () =>
        {
            var file = DeployFile.Read(filePath);
            var env = file.Environment(environment);
            if (!file.Secrets.Contains(name))
                throw new InvalidOperationException(
                    $"'{name}' is not listed under secrets in {DeployFile.FileName}. Add it there first, so every "
                        + "deploy knows to supply it."
                );
            var value = readValue();
            if (value.Length == 0)
                throw new InvalidOperationException("The value is empty; nothing was stored.");
            var stack = file.StackName(environment);
            await Client(env.Region)
                .PutSecretAsync(
                    AwsStack.AppSecretName(stack, name),
                    value,
                    AwsDeployer.Tags(file, environment),
                    ct
                );
            Console.WriteLine(
                $"Stored {AwsStack.AppSecretName(stack, name)}. Deploy again for the services to read it."
            );
        });

    internal static async Task<int> SecretListAsync(
        string filePath,
        string environment,
        CancellationToken ct
    ) =>
        await Guard(async () =>
        {
            var file = DeployFile.Read(filePath);
            var env = file.Environment(environment);
            var stack = file.StackName(environment);
            var stored = (
                await Client(env.Region).ListSecretsAsync(AwsStack.AppSecretName(stack, ""), ct)
            ).ToDictionary(s => s.Name, s => s.Changed, StringComparer.Ordinal);
            foreach (var name in file.Secrets)
                Console.WriteLine(
                    stored.TryGetValue(AwsStack.AppSecretName(stack, name), out var changed)
                        ? $"  {name, -32}set {changed:yyyy-MM-dd HH:mm}"
                        : $"  {name, -32}NOT SET"
                );
        });

    /// <summary>The value from standard input when it is redirected, else typed without echo.</summary>
    private static string ReadSecret()
    {
        if (Console.IsInputRedirected)
            return Console.In.ReadToEnd().TrimEnd('\r', '\n');
        Console.Write("Value (not shown): ");
        var value = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length > 0)
                    value.Length--;
                continue;
            }
            value.Append(key.KeyChar);
        }
        Console.WriteLine();
        return value.ToString();
    }

    private static async Task<int> Guard(Func<Task> action)
    {
        try
        {
            await action();
            return 0;
        }
        catch (Exception ex)
            when (ex
                    is InvalidOperationException
                        or IOException
                        or Amazon.Runtime.AmazonServiceException
                        or Amazon.Runtime.AmazonClientException
            )
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
