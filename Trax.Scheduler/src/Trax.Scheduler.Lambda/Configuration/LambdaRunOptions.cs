using Amazon.Lambda;

namespace Trax.Scheduler.Lambda.Configuration;

/// <summary>
/// Configuration options for offloading synchronous run execution to an AWS Lambda function via <c>UseLambdaRun()</c>.
/// </summary>
/// <remarks>
/// Run requests are sent as direct Lambda invocations using <c>InvocationType.RequestResponse</c> (synchronous).
/// The caller blocks until the Lambda function completes and returns the train output.
///
/// Access is controlled by IAM policies — no public endpoint is created. The calling service
/// needs <c>lambda:InvokeFunction</c> permission on the target function.
/// </remarks>
public class LambdaRunOptions
{
    /// <summary>
    /// The Lambda function name, ARN, or partial ARN to invoke.
    /// </summary>
    /// <example>content-shield-runner</example>
    public string FunctionName { get; set; } = null!;

    /// <summary>
    /// Optional callback to configure the <see cref="AmazonLambdaConfig"/> used to create the Lambda client.
    /// Use this to set a custom region, endpoint override (e.g., LocalStack), or service URL.
    /// </summary>
    public Action<AmazonLambdaConfig>? ConfigureLambdaClient { get; set; }
}
