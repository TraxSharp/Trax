using Amazon.Lambda;

namespace Trax.Scheduler.Lambda.Configuration;

/// <summary>
/// Configuration options for dispatching jobs to an AWS Lambda function via <c>UseLambdaWorkers()</c>.
/// </summary>
/// <remarks>
/// Jobs are sent as direct Lambda invocations using <c>InvocationType.Event</c> (fire-and-forget).
/// The Lambda function receives a <see cref="Services.Lambda.LambdaEnvelope"/> payload and executes
/// the train via <c>TraxLambdaFunction</c>.
///
/// Access is controlled by IAM policies — no public endpoint is created. The calling service
/// needs <c>lambda:InvokeFunction</c> permission on the target function.
/// </remarks>
public class LambdaWorkerOptions
{
    /// <summary>
    /// The Lambda function name, ARN, or partial ARN to invoke.
    /// </summary>
    /// <example>content-shield-runner</example>
    /// <example>arn:aws:lambda:us-east-1:123456789012:function:content-shield-runner</example>
    public string FunctionName { get; set; } = null!;

    /// <summary>
    /// Optional callback to configure the <see cref="AmazonLambdaConfig"/> used to create the Lambda client.
    /// Use this to set a custom region, endpoint override (e.g., LocalStack), or service URL.
    /// </summary>
    public Action<AmazonLambdaConfig>? ConfigureLambdaClient { get; set; }
}
