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

    public async Task<IReadOnlyList<AwsNetworkInterface>> NetworkInterfacesAsync(
        string vpcId,
        CancellationToken ct
    )
    {
        using var ec2 = new AmazonEC2Client(_region);
        var found = new List<AwsNetworkInterface>();
        string? next = null;
        do
        {
            var page = await ec2.DescribeNetworkInterfacesAsync(
                    new DescribeNetworkInterfacesRequest
                    {
                        Filters = [new Amazon.EC2.Model.Filter("vpc-id", [vpcId])],
                        NextToken = next,
                    },
                    ct
                )
                .ConfigureAwait(false);
            foreach (var eni in page.NetworkInterfaces ?? [])
                found.Add(
                    new AwsNetworkInterface(
                        eni.NetworkInterfaceId,
                        eni.InterfaceType?.Value ?? "",
                        eni.Description ?? "",
                        [.. (eni.Groups ?? []).Select(g => g.GroupId)],
                        eni.RequesterId ?? ""
                    )
                );
            next = page.NextToken;
        } while (!string.IsNullOrEmpty(next));
        return found;
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
        IReadOnlySet<string> unreplaceable,
        Action<string> progress,
        CancellationToken ct
    )
    {
        using var cfn = new AmazonCloudFormationClient(_region);
        var existing = await DescribeStackAsync(stack, ct).ConfigureAwait(false);
        var started = clock.GetUtcNow().UtcDateTime;
        var changeSet = await cfn.CreateChangeSetAsync(
                ChangeSetRequest(stack, template, parameters, tags, existing, started),
                ct
            )
            .ConfigureAwait(false);

        try
        {
            var described = await WaitForChangeSetAsync(cfn, changeSet.Id, stack, ct)
                .ConfigureAwait(false);
            if (described is null)
                return false;
            ReportChanges(described, progress);
            var replaced = Replaced(described.Changes ?? [], unreplaceable);
            if (replaced.Count > 0)
            {
                await cfn.DeleteChangeSetAsync(
                        new DeleteChangeSetRequest { ChangeSetName = changeSet.Id },
                        ct
                    )
                    .ConfigureAwait(false);
                throw new InvalidOperationException(
                    $"{stack} would replace {string.Join(", ", replaced)}, which running services still use, so "
                        + "the old ones could not be deleted and would be left behind. Nothing was changed. To move "
                        + "the environment to another network, destroy it and deploy again."
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

    // The change set request: CREATE or UPDATE as the stack stands, named for when the deploy started.
    private static CreateChangeSetRequest ChangeSetRequest(
        string stack,
        string? template,
        IReadOnlyDictionary<string, string?> parameters,
        IReadOnlyDictionary<string, string> tags,
        AwsStackState? existing,
        DateTime started
    ) =>
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
        };

    // Waits until the change set is ready. Null when it holds no change, once it is deleted; throws when
    // CloudFormation refused it for any other reason.
    private async Task<DescribeChangeSetResponse?> WaitForChangeSetAsync(
        AmazonCloudFormationClient cfn,
        string changeSetId,
        string stack,
        CancellationToken ct
    )
    {
        while (true)
        {
            var described = await cfn.DescribeChangeSetAsync(
                    new DescribeChangeSetRequest { ChangeSetName = changeSetId },
                    ct
                )
                .ConfigureAwait(false);
            if (described.Status == ChangeSetStatus.CREATE_COMPLETE)
                return described;
            if (described.Status == ChangeSetStatus.FAILED)
            {
                await cfn.DeleteChangeSetAsync(
                        new DeleteChangeSetRequest { ChangeSetName = changeSetId },
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
                    return null;
                throw new InvalidOperationException(
                    $"CloudFormation refused the change to {stack}: {described.StatusReason}"
                );
            }
            await Task.Delay(TimeSpan.FromSeconds(3), clock, ct).ConfigureAwait(false);
        }
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
            throw StackFailed(stack, status, events, since);
        }
    }

    public async Task<AwsTaskResult> RunTaskAsync(
        AwsTaskRequest request,
        Action<string> progress,
        CancellationToken ct
    )
    {
        using var ecs = new AmazonECSClient(_region);
        var run = await ecs.RunTaskAsync(MigrateTaskRequest(request), ct).ConfigureAwait(false);
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

    /// <summary>
    /// The resources of <paramref name="keep"/> that <paramref name="changes"/> replace, or may replace.
    /// </summary>
    internal static IReadOnlyList<string> Replaced(
        IEnumerable<Change> changes,
        IReadOnlySet<string> keep
    ) =>
        [
            .. changes
                .Select(c => c.ResourceChange)
                .Where(r =>
                    r is not null
                    && keep.Contains(r.LogicalResourceId)
                    && r.Replacement?.Value is "True" or "Conditional"
                )
                .Select(r => r.LogicalResourceId),
        ];

    private static void ReportChanges(DescribeChangeSetResponse described, Action<string> progress)
    {
        foreach (var change in described.Changes ?? [])
        {
            var resource = change.ResourceChange;
            progress(
                $"  {resource.Action?.Value, -8}{resource.LogicalResourceId} ({resource.ResourceType})"
                    + (
                        string.Equals(resource.Replacement?.Value, "True", StringComparison.Ordinal)
                            ? ", replaced"
                            : ""
                    )
            );
        }
    }

    // What a stack that did not end complete fails with: its status and each failed resource's reason.
    private static InvalidOperationException StackFailed(
        string stack,
        string status,
        List<StackEvent> events,
        DateTime since
    )
    {
        var reasons = events
            .Where(e =>
                e.Timestamp >= since
                && e.ResourceStatus?.Value.EndsWith("_FAILED", StringComparison.Ordinal) is true
            )
            .Select(e => $"{e.LogicalResourceId}: {e.ResourceStatusReason}")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return new InvalidOperationException(
            $"Stack {stack} ended {status}."
                + (reasons.Count == 0 ? "" : "\n  " + string.Join("\n  ", reasons))
        );
    }

    private static RunTaskRequest MigrateTaskRequest(AwsTaskRequest request) =>
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
                    SecurityGroups = [.. request.SecurityGroups],
                    AssignPublicIp = request.AssignPublicIp
                        ? AssignPublicIp.ENABLED
                        : AssignPublicIp.DISABLED,
                },
            },
        };

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
        await PutSecretAsync(secrets, name, value, tags, ct).ConfigureAwait(false);
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
            return (
                await ssm.GetParameterAsync(new GetParameterRequest { Name = name }, ct)
                    .ConfigureAwait(false)
            )
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
                )
                .ConfigureAwait(false);
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
                )
                .ConfigureAwait(false);
        }
    }

    public async Task DeleteParameterAsync(string name, CancellationToken ct)
    {
        using var ssm = new AmazonSimpleSystemsManagementClient(_region);
        try
        {
            await ssm.DeleteParameterAsync(new DeleteParameterRequest { Name = name }, ct)
                .ConfigureAwait(false);
        }
        catch (ParameterNotFoundException) { }
    }

    public async Task<long?> UserCountAsync(string userPoolId, CancellationToken ct)
    {
        using var cognito = new AmazonCognitoIdentityProviderClient(_region);
        try
        {
            var pool = await cognito
                .DescribeUserPoolAsync(new DescribeUserPoolRequest { UserPoolId = userPoolId }, ct)
                .ConfigureAwait(false);
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
                await cognito
                    .DescribeUserPoolAsync(
                        new DescribeUserPoolRequest { UserPoolId = userPoolId },
                        ct
                    )
                    .ConfigureAwait(false)
            ).UserPool;
            if (pool.DeletionProtection == DeletionProtectionType.ACTIVE)
                await cognito
                    .UpdateUserPoolAsync(UnprotectedUpdate(pool), ct)
                    .ConfigureAwait(false);
            await cognito
                .DeleteUserPoolAsync(new DeleteUserPoolRequest { UserPoolId = userPoolId }, ct)
                .ConfigureAwait(false);
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
