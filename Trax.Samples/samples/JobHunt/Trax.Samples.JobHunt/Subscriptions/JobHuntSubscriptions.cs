using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Subscriptions;
using HotChocolate.Types;

namespace Trax.Samples.JobHunt.Subscriptions;

[ExtendObjectType(OperationTypeNames.Subscription)]
public class JobHuntSubscriptions
{
    [Subscribe]
    [Topic("User:{userId}:jobs")]
    public JobHuntSubscriptionEvent OnUserJobs(
        string userId,
        [EventMessage] JobHuntSubscriptionEvent message
    ) => message;

    [Subscribe]
    [Topic("Job:{jobId}:materials")]
    public JobHuntSubscriptionEvent OnJobMaterials(
        Guid jobId,
        [EventMessage] JobHuntSubscriptionEvent message
    ) => message;

    [Subscribe]
    [Topic("Job:{jobId}:monitor")]
    public JobHuntSubscriptionEvent OnJobMonitor(
        Guid jobId,
        [EventMessage] JobHuntSubscriptionEvent message
    ) => message;

    [Subscribe]
    [Topic("User:{userId}:notifications")]
    public JobHuntSubscriptionEvent OnUserNotifications(
        string userId,
        [EventMessage] JobHuntSubscriptionEvent message
    ) => message;
}
