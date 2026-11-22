using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Amazon;
using Amazon.CloudFormation;
using Amazon.CloudFormation.Model;
using Amazon.CloudWatchLogs;
using Amazon.CloudWatchLogs.Model;
using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.ECR;
using Amazon.ECR.Model;
using Amazon.ECS;
using Amazon.ECS.Model;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Amazon.SimpleSystemsManagement;
using Amazon.SimpleSystemsManagement.Model;
using Trax.Core.Extensions;
using InvalidOperationException = System.InvalidOperationException;
using Parameter = Amazon.CloudFormation.Model.Parameter;
using Task = System.Threading.Tasks.Task;

namespace Trax.Cli.Deploy.Aws;

/// <summary>
/// <see cref="IAwsDeployClient"/> over the AWS SDK for .NET. Credentials come from the standard chain (a profile,
/// SSO, the environment, or a role in CI); trax never stores one.
/// </summary>
internal sealed class AwsDeployClient(string region, TimeProvider clock) : IAwsDeployClient
{
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(10);

    private readonly RegionEndpoint _region = RegionEndpoint.GetBySystemName(region);

    public string Region => region;

    public async Task<string> AccountIdAsync(CancellationToken ct)
    {
        using var sts = new AmazonSecurityTokenServiceClient(_region);
        return (
            await sts.GetCallerIdentityAsync(new GetCallerIdentityRequest(), ct)
                .ConfigureAwait(false)
        ).Account;
    }

    public async Task<string?> CloudFrontPrefixListAsync(CancellationToken ct)
    {
        using var ec2 = new AmazonEC2Client(_region);
        var lists = await ec2.DescribeManagedPrefixListsAsync(
                new DescribeManagedPrefixListsRequest
                {
                    Filters =
                    [
                        new Amazon.EC2.Model.Filter(
                            "prefix-list-name",
                            ["com.amazonaws.global.cloudfront.origin-facing"]
                        ),
                    ],
                },
                ct
            )
            .ConfigureAwait(false);
        return lists.PrefixLists?.FirstOrDefault()?.PrefixListId;
    }

    public async Task<string> EnsureRepositoryAsync(
        string name,
        IReadOnlyDictionary<string, string> tags,
        CancellationToken ct
    )
    {
        using var ecr = new AmazonECRClient(_region);
        try
        {
            var found = await ecr.DescribeRepositoriesAsync(
                    new DescribeRepositoriesRequest { RepositoryNames = [name] },
                    ct
                )
                .ConfigureAwait(false);
            return found.Repositories.NotNull()[0].RepositoryUri;
        }
        catch (RepositoryNotFoundException)
        {
            var created = await ecr.CreateRepositoryAsync(
                    new CreateRepositoryRequest
                    {
                        RepositoryName = name,
                        // Each deploy pushes a new tag, so no tag ever points at two images.
                        ImageTagMutability = ImageTagMutability.IMMUTABLE,
                        ImageScanningConfiguration = new ImageScanningConfiguration
                        {
                            ScanOnPush = true,
                        },
                        Tags =
                        [
                            .. tags.Select(t => new Amazon.ECR.Model.Tag
                            {
                                Key = t.Key,
                                Value = t.Value,
                            }),
                        ],
                    },
                    ct
                )
                .ConfigureAwait(false);
            return created.Repository.RepositoryUri;
        }
    }

    public async Task<RegistryLogin> RegistryLoginAsync(CancellationToken ct)
    {
        using var ecr = new AmazonECRClient(_region);
        var data = (
            await ecr.GetAuthorizationTokenAsync(new GetAuthorizationTokenRequest(), ct)
                .ConfigureAwait(false)
        ).AuthorizationData.NotNull()[0];
        var userAndPassword = Encoding.UTF8.GetString(
            Convert.FromBase64String(data.AuthorizationToken)
        );
        var colon = userAndPassword.IndexOf(':', StringComparison.Ordinal);
        return new RegistryLogin(
            new Uri(data.ProxyEndpoint).Host,
            userAndPassword[..colon],
            userAndPassword[(colon + 1)..]
        );
    }

    public async Task<AwsStackState?> DescribeStackAsync(string stack, CancellationToken ct)
    {
        using var cfn = new AmazonCloudFormationClient(_region);
        try
        {
            var found = (
                await cfn.DescribeStacksAsync(new DescribeStacksRequest { StackName = stack }, ct)
                    .ConfigureAwait(false)
            ).Stacks.NotNull()[0];
            return new AwsStackState(
                found.StackStatus.Value,
                (found.Outputs ?? []).ToDictionary(
                    o => o.OutputKey,
                    o => o.OutputValue,
                    StringComparer.Ordinal
                ),
                (found.Parameters ?? []).ToDictionary(
                    p => p.ParameterKey,
                    p => p.ParameterValue,
                    StringComparer.Ordinal
                )
            )
            {
                Tags = (found.Tags ?? []).ToDictionary(
                    t => t.Key,
                    t => t.Value,
                    StringComparer.Ordinal
                ),
            };
        }
        catch (AmazonCloudFormationException ex)
            when (ex.Message.Contains("does not exist", StringComparison.Ordinal))
        {
            return null;
        }
    }

    public async Task<bool> DeployAsync(
        string stack,
        string? template,
        IReadOnlyDictionary<string, string?> parameters,
        IReadOnlyDictionary<string, string> tags,
        Action<string> progress,
        CancellationToken ct
    )
    {
        using var cfn = new AmazonCloudFormationClient(_region);
        var existing = await DescribeStackAsync(stack, ct).ConfigureAwait(false);
        var started = clock.GetUtcNow().UtcDateTime;
        var changeSet = await cfn.CreateChangeSetAsync(
                new CreateChangeSetRequest
                {
                    StackName = stack,
                    ChangeSetName = string.Create(
                        CultureInfo.InvariantCulture,
                        $"trax-{started:yyyyMMddHHmmss}"
                    ),
                    ChangeSetType = ChangeSetTypeFor(existing),
                    TemplateBody = template,
                    UsePreviousTemplate = template is null ? true : null,
                    Parameters =
                    [
                        .. parameters.Select(p =>
                            p.Value is null
                                ? new Parameter { ParameterKey = p.Key, UsePreviousValue = true }
                                : new Parameter { ParameterKey = p.Key, ParameterValue = p.Value }
                        ),
                    ],
                    Capabilities = ["CAPABILITY_IAM"],
                    Tags =
                    [
                        .. tags.Select(t => new Amazon.CloudFormation.Model.Tag
                        {
                            Key = t.Key,
                            Value = t.Value,
                        }),
                    ],
                },
                ct
            )
            .ConfigureAwait(false);

        try
        {
            DescribeChangeSetResponse described;
            while (true)
            {
                described = await cfn.DescribeChangeSetAsync(
                        new DescribeChangeSetRequest { ChangeSetName = changeSet.Id },
                        ct
                    )
                    .ConfigureAwait(false);
                if (described.Status == ChangeSetStatus.CREATE_COMPLETE)
                    break;
                if (described.Status == ChangeSetStatus.FAILED)
                {
                    await cfn.DeleteChangeSetAsync(
                            new DeleteChangeSetRequest { ChangeSetName = changeSet.Id },
                            ct
                        )
                        .ConfigureAwait(false);
                    if (
                        described.StatusReason?.Contains(
                            "didn't contain changes",
                            StringComparison.Ordinal
                        )
                            is true
                        || described.StatusReason?.Contains("No updates", StringComparison.Ordinal)
                            is true
                    )
                        return false;
                    throw new InvalidOperationException(
                        $"CloudFormation refused the change to {stack}: {described.StatusReason}"
                    );
                }
                await Task.Delay(TimeSpan.FromSeconds(3), clock, ct).ConfigureAwait(false);
            }
            foreach (var change in described.Changes ?? [])
            {
                var resource = change.ResourceChange;
                progress(
                    $"  {resource.Action?.Value, -8}{resource.LogicalResourceId} ({resource.ResourceType})"
                        + (
                            string.Equals(
                                resource.Replacement?.Value,
                                "True",
                                StringComparison.Ordinal
                            )
                                ? ", replaced"
                                : ""
                        )
                );
            }

            await cfn.ExecuteChangeSetAsync(
                    new ExecuteChangeSetRequest { ChangeSetName = changeSet.Id },
                    ct
                )
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A change set left behind holds a new stack in REVIEW_IN_PROGRESS. The next deploy creates it again
            // either way, so a delete that fails (the change set already executing) changes nothing.
            try
            {
                await cfn.DeleteChangeSetAsync(
                        new DeleteChangeSetRequest { ChangeSetName = changeSet.Id },
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
            }
            catch (AmazonCloudFormationException) { }
            throw;
        }
        await WaitAsync(cfn, stack, started, progress, ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// CREATE for a stack that does not exist or holds nothing yet (REVIEW_IN_PROGRESS, left by a first deploy's
    /// change set that failed or was abandoned, which CloudFormation lets a CREATE change set replace), else UPDATE.
    /// </summary>
    internal static ChangeSetType ChangeSetTypeFor(AwsStackState? existing) =>
        existing is null or { Created: false } ? ChangeSetType.CREATE : ChangeSetType.UPDATE;

    private async Task WaitAsync(
        AmazonCloudFormationClient cfn,
        string stack,
        DateTime since,
        Action<string> progress,
        CancellationToken ct
    )
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            await Task.Delay(Poll, clock, ct).ConfigureAwait(false);
            var events =
                (
                    await cfn.DescribeStackEventsAsync(
                            new DescribeStackEventsRequest { StackName = stack },
                            ct
                        )
                        .ConfigureAwait(false)
                ).StackEvents
                ?? [];
            var fresh = events
                .Where(e => e.Timestamp >= since && seen.Add(e.EventId))
                .OrderBy(e => e.Timestamp)
                .ToList();
            foreach (var e in fresh)
                progress(
                    $"  {e.ResourceStatus?.Value, -28}{e.LogicalResourceId}"
                        + (
                            string.IsNullOrEmpty(e.ResourceStatusReason)
                                ? ""
                                : $": {e.ResourceStatusReason}"
                        )
                );

            Amazon.CloudFormation.Model.Stack current;
            try
            {
                current = (
                    await cfn.DescribeStacksAsync(
                            new DescribeStacksRequest { StackName = stack },
                            ct
                        )
                        .ConfigureAwait(false)
                ).Stacks.NotNull()[0];
            }
            catch (AmazonCloudFormationException ex)
                when (ex.Message.Contains("does not exist", StringComparison.Ordinal))
            {
                return;
            }
            var status = current.StackStatus.Value;
            if (status.EndsWith("_IN_PROGRESS", StringComparison.Ordinal))
                continue;
            if (status is "CREATE_COMPLETE" or "UPDATE_COMPLETE" or "DELETE_COMPLETE")
                return;
            var reasons = events
                .Where(e =>
                    e.Timestamp >= since
                    && e.ResourceStatus?.Value.EndsWith("_FAILED", StringComparison.Ordinal) is true
                )
                .Select(e => $"{e.LogicalResourceId}: {e.ResourceStatusReason}")
                .Distinct(StringComparer.Ordinal)
                .ToList();
            throw new InvalidOperationException(
                $"Stack {stack} ended {status}."
                    + (reasons.Count == 0 ? "" : "\n  " + string.Join("\n  ", reasons))
            );
        }
    }

    public async Task<AwsTaskResult> RunTaskAsync(
        AwsTaskRequest request,
        Action<string> progress,
        CancellationToken ct
    )
    {
        using var ecs = new AmazonECSClient(_region);
        var run = await ecs.RunTaskAsync(
                new RunTaskRequest
                {
                    Cluster = request.Cluster,
                    TaskDefinition = request.TaskDefinition,
                    LaunchType = LaunchType.FARGATE,
                    Count = 1,
                    StartedBy = "trax-deploy",
                    NetworkConfiguration = new NetworkConfiguration
                    {
                        AwsvpcConfiguration = new AwsVpcConfiguration
                        {
                            Subnets = [.. request.Subnets],
                            SecurityGroups = [request.SecurityGroup],
                            AssignPublicIp = request.AssignPublicIp
                                ? AssignPublicIp.ENABLED
                                : AssignPublicIp.DISABLED,
                        },
                    },
                },
                ct
            )
            .ConfigureAwait(false);
        var task =
            run.Tasks?.FirstOrDefault()
            ?? throw new InvalidOperationException(
                "ECS did not start the migrate task: "
                    + string.Join("; ", (run.Failures ?? []).Select(f => $"{f.Arn} {f.Reason}"))
            );
        var id = task.TaskArn[(task.TaskArn.LastIndexOf('/') + 1)..];
        var logStream = $"{request.Container}/{request.Container}/{id}";
        var last = "";
        while (true)
        {
            await Task.Delay(Poll, clock, ct).ConfigureAwait(false);
            var current = (
                await ecs.DescribeTasksAsync(
                        new DescribeTasksRequest
                        {
                            Cluster = request.Cluster,
                            Tasks = [task.TaskArn],
                        },
                        ct
                    )
                    .ConfigureAwait(false)
            ).Tasks.NotNull()[0];
            if (!string.Equals(current.LastStatus, last, StringComparison.Ordinal))
                progress($"  migrate task {current.LastStatus}");
            last = current.LastStatus;
            if (!string.Equals(current.LastStatus, "STOPPED", StringComparison.Ordinal))
                continue;
            var container = current.Containers?.FirstOrDefault(c =>
                string.Equals(c.Name, request.Container, StringComparison.Ordinal)
            );
            return new AwsTaskResult(
                container?.ExitCode,
                current.StoppedReason ?? container?.Reason,
                logStream
            );
        }
    }

    public async Task DeleteStackAsync(string stack, Action<string> progress, CancellationToken ct)
    {
        using var cfn = new AmazonCloudFormationClient(_region);
        var started = clock.GetUtcNow().UtcDateTime;
        await cfn.DeleteStackAsync(new DeleteStackRequest { StackName = stack }, ct)
            .ConfigureAwait(false);
        await WaitAsync(cfn, stack, started, progress, ct).ConfigureAwait(false);
    }

    public async Task DeleteRepositoryAsync(string name, CancellationToken ct)
    {
        using var ecr = new AmazonECRClient(_region);
        try
        {
            await ecr.DeleteRepositoryAsync(
                    new DeleteRepositoryRequest { RepositoryName = name, Force = true },
                    ct
                )
                .ConfigureAwait(false);
        }
        catch (RepositoryNotFoundException) { }
    }

    public async Task PutSecretAsync(
        string name,
        string value,
        IReadOnlyDictionary<string, string> tags,
        CancellationToken ct
    )
    {
        using var secrets = new AmazonSecretsManagerClient(_region);
        await PutSecretAsync(secrets, name, value, tags, ct);
    }

    /// <summary>
    /// Stores <paramref name="value"/> as the secret's current value, creating the secret when it does not exist and
    /// restoring it first when it is pending deletion (as <c>destroy --delete-data</c> leaves it for seven days),
    /// since a secret pending deletion takes no value and holds its name.
    /// </summary>
    internal static async Task PutSecretAsync(
        IAmazonSecretsManager secrets,
        string name,
        string value,
        IReadOnlyDictionary<string, string> tags,
        CancellationToken ct
    )
    {
        var put = new PutSecretValueRequest { SecretId = name, SecretString = value };
        try
        {
            await secrets.PutSecretValueAsync(put, ct).ConfigureAwait(false);
        }
        catch (InvalidRequestException ex)
            when (ex.Message.Contains("marked for deletion", StringComparison.Ordinal))
        {
            await secrets
                .RestoreSecretAsync(new RestoreSecretRequest { SecretId = name }, ct)
                .ConfigureAwait(false);
            await secrets.PutSecretValueAsync(put, ct).ConfigureAwait(false);
        }
        catch (Amazon.SecretsManager.Model.ResourceNotFoundException)
        {
            await secrets
                .CreateSecretAsync(
                    new CreateSecretRequest
                    {
                        Name = name,
                        SecretString = value,
                        Tags =
                        [
                            .. tags.Select(t => new Amazon.SecretsManager.Model.Tag
                            {
                                Key = t.Key,
                                Value = t.Value,
                            }),
                        ],
                    },
                    ct
                )
                .ConfigureAwait(false);
        }
    }

    public async Task DeleteSecretAsync(string name, CancellationToken ct)
    {
        using var secrets = new AmazonSecretsManagerClient(_region);
        try
        {
            // Recoverable for seven days, as a deleted secret is by default.
            await secrets
                .DeleteSecretAsync(new DeleteSecretRequest { SecretId = name }, ct)
                .ConfigureAwait(false);
        }
        catch (Amazon.SecretsManager.Model.ResourceNotFoundException) { }
    }

    public async Task<IReadOnlyList<AwsSecret>> ListSecretsAsync(
        string prefix,
        CancellationToken ct
    )
    {
        using var secrets = new AmazonSecretsManagerClient(_region);
        var found = new List<AwsSecret>();
        string? next = null;
        do
        {
            var page = await secrets
                .ListSecretsAsync(
                    new ListSecretsRequest
                    {
                        Filters =
                        [
                            new Amazon.SecretsManager.Model.Filter
                            {
                                Key = FilterNameStringType.Name,
                                Values = [prefix],
                            },
                        ],
                        NextToken = next,
                    },
                    ct
                )
                .ConfigureAwait(false);
            found.AddRange(
                (page.SecretList ?? [])
                    .Where(s => s.Name.StartsWith(prefix, StringComparison.Ordinal))
                    .Select(s => new AwsSecret(s.Name, s.LastChangedDate))
            );
            next = page.NextToken;
        } while (next is not null);
        return found;
    }

    public async Task<string?> GetParameterAsync(string name, CancellationToken ct)
    {
        using var ssm = new AmazonSimpleSystemsManagementClient(_region);
        try
        {
            return (await ssm.GetParameterAsync(new GetParameterRequest { Name = name }, ct))
                .Parameter
                .Value;
        }
        catch (ParameterNotFoundException)
        {
            return null;
        }
    }

    public async Task PutParameterAsync(
        string name,
        string value,
        IReadOnlyDictionary<string, string> tags,
        CancellationToken ct
    )
    {
        using var ssm = new AmazonSimpleSystemsManagementClient(_region);
        try
        {
            await ssm.PutParameterAsync(
                new PutParameterRequest
                {
                    Name = name,
                    Value = value,
                    Type = ParameterType.String,
                    Tags =
                    [
                        .. tags.Select(t => new Amazon.SimpleSystemsManagement.Model.Tag
                        {
                            Key = t.Key,
                            Value = t.Value,
                        }),
                    ],
                },
                ct
            );
        }
        catch (ParameterAlreadyExistsException)
        {
            // Tags go only with a new parameter; an existing one is overwritten without them.
            await ssm.PutParameterAsync(
                new PutParameterRequest
                {
                    Name = name,
                    Value = value,
                    Type = ParameterType.String,
                    Overwrite = true,
                },
                ct
            );
        }
    }

    public async Task DeleteParameterAsync(string name, CancellationToken ct)
    {
        using var ssm = new AmazonSimpleSystemsManagementClient(_region);
        try
        {
            await ssm.DeleteParameterAsync(new DeleteParameterRequest { Name = name }, ct);
        }
        catch (ParameterNotFoundException) { }
    }

    public async Task<long?> UserCountAsync(string userPoolId, CancellationToken ct)
    {
        using var cognito = new AmazonCognitoIdentityProviderClient(_region);
        try
        {
            var pool = await cognito.DescribeUserPoolAsync(
                new DescribeUserPoolRequest { UserPoolId = userPoolId },
                ct
            );
            return pool.UserPool.EstimatedNumberOfUsers ?? 0;
        }
        catch (Amazon.CognitoIdentityProvider.Model.ResourceNotFoundException)
        {
            return null;
        }
    }

    public async Task DeleteUserPoolAsync(string userPoolId, CancellationToken ct)
    {
        using var cognito = new AmazonCognitoIdentityProviderClient(_region);
        try
        {
            var pool = (
                await cognito.DescribeUserPoolAsync(
                    new DescribeUserPoolRequest { UserPoolId = userPoolId },
                    ct
                )
            ).UserPool;
            if (pool.DeletionProtection == DeletionProtectionType.ACTIVE)
                await cognito.UpdateUserPoolAsync(UnprotectedUpdate(pool), ct);
            await cognito.DeleteUserPoolAsync(
                new DeleteUserPoolRequest { UserPoolId = userPoolId },
                ct
            );
        }
        catch (Amazon.CognitoIdentityProvider.Model.ResourceNotFoundException) { }
    }

    /// <summary>
    /// The pool's own settings with deletion protection off. UpdateUserPool resets every setting it is not given to
    /// its default, so the ones the stack set are passed back as they are; the MFA setting, which needs its TOTP
    /// configuration with it, is left to the delete that follows.
    /// </summary>
    private static UpdateUserPoolRequest UnprotectedUpdate(UserPoolType pool) =>
        new()
        {
            UserPoolId = pool.Id,
            DeletionProtection = DeletionProtectionType.INACTIVE,
            AutoVerifiedAttributes = pool.AutoVerifiedAttributes,
            AdminCreateUserConfig = pool.AdminCreateUserConfig,
            Policies = pool.Policies,
        };

    public async Task<IReadOnlyList<AwsServiceState>> DescribeServicesAsync(
        string cluster,
        IReadOnlyList<string> services,
        CancellationToken ct
    )
    {
        using var ecs = new AmazonECSClient(_region);
        var found = await ecs.DescribeServicesAsync(
                new DescribeServicesRequest { Cluster = cluster, Services = [.. services] },
                ct
            )
            .ConfigureAwait(false);
        return
        [
            .. (found.Services ?? []).Select(s => new AwsServiceState(
                s.ServiceName,
                s.DesiredCount ?? 0,
                s.RunningCount ?? 0,
                s.PendingCount ?? 0,
                s.Deployments?.FirstOrDefault()?.RolloutState?.Value ?? "",
                s.TaskDefinition[(s.TaskDefinition.LastIndexOf('/') + 1)..]
            )),
        ];
    }

    public async IAsyncEnumerable<string> LogsAsync(
        string group,
        DateTime since,
        bool follow,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        using var logs = new AmazonCloudWatchLogsClient(_region);
        var start = new DateTimeOffset(since, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var seen = new SeenLogEvents();
        while (true)
        {
            string? next = null;
            do
            {
                var page = await logs.FilterLogEventsAsync(
                        new FilterLogEventsRequest
                        {
                            LogGroupName = group,
                            StartTime = start,
                            NextToken = next,
                        },
                        ct
                    )
                    .ConfigureAwait(false);
                foreach (var e in page.Events ?? [])
                    if (seen.Add(e.EventId, e.Timestamp ?? start))
                    {
                        start = Math.Max(start, e.Timestamp ?? start);
                        yield return e.Message.TrimEnd();
                    }
                next = page.NextToken;
            } while (next is not null);
            if (!follow)
                yield break;
            await Task.Delay(TimeSpan.FromSeconds(2), clock, ct).ConfigureAwait(false);
        }
    }
}
