namespace Trax.Cli.Deploy.Aws;

/// <summary>
/// The AWS calls <c>trax deploy</c> makes, so the deploy can be tested without an account. <see cref="AwsDeployClient"/>
/// makes them with the AWS SDK, under the standard credential chain.
/// </summary>
internal interface IAwsDeployClient
{
    string Region { get; }

    Task<string> AccountIdAsync(CancellationToken ct);

    /// <summary>The region's CloudFront origin-facing prefix list, or null when there is none.</summary>
    Task<string?> CloudFrontPrefixListAsync(CancellationToken ct);

    /// <summary>Creates the repository when it does not exist, and returns its URI (without a tag).</summary>
    Task<string> EnsureRepositoryAsync(
        string name,
        IReadOnlyDictionary<string, string> tags,
        CancellationToken ct
    );

    /// <summary>A short-lived login for the account's registry.</summary>
    Task<RegistryLogin> RegistryLoginAsync(CancellationToken ct);

    Task<AwsStackState?> DescribeStackAsync(string stack, CancellationToken ct);

    /// <summary>
    /// Creates or updates <paramref name="stack"/> through a change set and waits until it settles. A parameter whose
    /// value is null keeps its previous value; a null <paramref name="template"/> keeps the previous template.
    /// Returns false when there was nothing to change. Throws naming the resources that failed.
    /// </summary>
    Task<bool> DeployAsync(
        string stack,
        string? template,
        IReadOnlyDictionary<string, string?> parameters,
        IReadOnlyDictionary<string, string> tags,
        Action<string> progress,
        CancellationToken ct
    );

    /// <summary>Runs one task to completion and returns how its container ended.</summary>
    Task<AwsTaskResult> RunTaskAsync(
        AwsTaskRequest request,
        Action<string> progress,
        CancellationToken ct
    );

    Task DeleteStackAsync(string stack, Action<string> progress, CancellationToken ct);

    Task DeleteRepositoryAsync(string name, CancellationToken ct);

    Task PutSecretAsync(
        string name,
        string value,
        IReadOnlyDictionary<string, string> tags,
        CancellationToken ct
    );

    Task DeleteSecretAsync(string name, CancellationToken ct);

    /// <summary>The secrets whose names start with <paramref name="prefix"/>, with when each last changed.</summary>
    Task<IReadOnlyList<AwsSecret>> ListSecretsAsync(string prefix, CancellationToken ct);

    /// <summary>The value of the SSM parameter <paramref name="name"/>, or null when there is none.</summary>
    Task<string?> GetParameterAsync(string name, CancellationToken ct);

    /// <summary>Creates or overwrites the SSM parameter <paramref name="name"/>.</summary>
    Task PutParameterAsync(
        string name,
        string value,
        IReadOnlyDictionary<string, string> tags,
        CancellationToken ct
    );

    /// <summary>Deletes the SSM parameter <paramref name="name"/>; nothing when there is none.</summary>
    Task DeleteParameterAsync(string name, CancellationToken ct);

    /// <summary>Roughly how many users the Cognito user pool holds, or null when the pool does not exist.</summary>
    Task<long?> UserCountAsync(string userPoolId, CancellationToken ct);

    /// <summary>Turns off the user pool's deletion protection and deletes it; nothing when it does not exist.</summary>
    Task DeleteUserPoolAsync(string userPoolId, CancellationToken ct);

    Task<IReadOnlyList<AwsServiceState>> DescribeServicesAsync(
        string cluster,
        IReadOnlyList<string> services,
        CancellationToken ct
    );

    /// <summary>The log group's lines, oldest first, from <paramref name="since"/>; with <paramref name="follow"/>, until cancelled.</summary>
    IAsyncEnumerable<string> LogsAsync(
        string group,
        DateTime since,
        bool follow,
        CancellationToken ct
    );
}

internal sealed record RegistryLogin(string Registry, string User, string Password);

internal sealed record AwsStackState(
    string Status,
    IReadOnlyDictionary<string, string> Outputs,
    IReadOnlyDictionary<string, string> Parameters
)
{
    /// <summary>The stack's tags: <c>trax:app</c> names the app that deployed it.</summary>
    public IReadOnlyDictionary<string, string> Tags { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Whether the stack is settled and can be changed.</summary>
    public bool Settled => !Status.EndsWith("_IN_PROGRESS", StringComparison.Ordinal);

    /// <summary>
    /// Whether the stack holds anything. A first deploy's change set makes the stack exist in REVIEW_IN_PROGRESS
    /// before it is executed; a stack left there by a change set that failed or was abandoned is created again.
    /// </summary>
    public bool Created => Status != "REVIEW_IN_PROGRESS";

    /// <summary>A stack whose creation failed and rolled back: it can only be deleted.</summary>
    public bool Dead => Status is "ROLLBACK_COMPLETE" or "ROLLBACK_FAILED" or "DELETE_FAILED";
}

internal sealed record AwsTaskRequest(
    string Cluster,
    string TaskDefinition,
    string Container,
    IReadOnlyList<string> Subnets,
    string SecurityGroup,
    bool AssignPublicIp
);

internal sealed record AwsTaskResult(int? ExitCode, string? StoppedReason, string LogStream);

internal sealed record AwsSecret(string Name, DateTime? Changed);

internal sealed record AwsServiceState(
    string Name,
    int Desired,
    int Running,
    int Pending,
    string Rollout,
    string TaskDefinition
);
