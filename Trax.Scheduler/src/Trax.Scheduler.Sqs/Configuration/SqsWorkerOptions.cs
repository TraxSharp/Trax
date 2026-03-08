using Amazon.SQS;

namespace Trax.Scheduler.Sqs.Configuration;

/// <summary>
/// Configuration options for dispatching jobs to an SQS queue via <c>UseSqsWorkers()</c>.
/// </summary>
public class SqsWorkerOptions
{
    /// <summary>
    /// The SQS queue URL to send messages to.
    /// </summary>
    /// <example>https://sqs.us-east-1.amazonaws.com/123456789012/trax-jobs</example>
    public string QueueUrl { get; set; } = null!;

    /// <summary>
    /// Optional callback to configure the <see cref="AmazonSQSConfig"/> used to create the SQS client.
    /// Use this to set a custom region, endpoint override (e.g., LocalStack), or service URL.
    /// </summary>
    public Action<AmazonSQSConfig>? ConfigureSqsClient { get; set; }

    /// <summary>
    /// Optional SQS message group ID for FIFO queues.
    /// When set, all messages use this group ID. When null and the queue URL ends with
    /// <c>.fifo</c>, each message gets a unique group ID (no ordering guarantee).
    /// Only applicable to FIFO queues; ignored for standard queues.
    /// </summary>
    public string? MessageGroupId { get; set; }
}
