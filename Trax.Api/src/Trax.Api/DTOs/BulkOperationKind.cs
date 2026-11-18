using Trax.Api.Services.BulkOperations;

namespace Trax.Api.DTOs;

/// <summary>What a bulk operation does to each id it matched.</summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/graphql-api/mutations/bulk-operations">SDK reference</seealso>
public enum BulkOperationKind
{
    /// <summary>Requests cancellation of each run, as <c>cancelExecutions</c> does.</summary>
    Cancel,

    /// <summary>Requeues each dead letter, as <c>requeueDeadLetters</c> does.</summary>
    Requeue,

    /// <summary>Acknowledges each dead letter with the operation's note, as <c>acknowledgeDeadLetters</c> does.</summary>
    Acknowledge,

    /// <summary>Triggers each manifest, as <c>triggerManifests</c> does.</summary>
    Trigger,

    /// <summary>Enables each manifest, as <c>setManifestsEnabled(enabled: true)</c> does.</summary>
    Enable,

    /// <summary>Disables each manifest, as <c>setManifestsEnabled(enabled: false)</c> does.</summary>
    Disable,
}
