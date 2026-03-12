using Amazon.SQS;
using Microsoft.Extensions.DependencyInjection;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Sqs.Configuration;
using Trax.Scheduler.Sqs.Services;

namespace Trax.Scheduler.Sqs.Extensions;

/// <summary>
/// Extension methods for configuring SQS-based job dispatch on the scheduler.
/// </summary>
public static class SqsSchedulerExtensions
{
    /// <summary>
    /// Routes specific trains to an SQS queue for execution by Lambda or another SQS consumer.
    /// </summary>
    /// <remarks>
    /// Trains not included in the <paramref name="routing"/> configuration continue to execute
    /// locally via <c>PostgresJobSubmitter</c> and <c>LocalWorkerService</c>.
    /// Only the trains specified via <c>ForTrain&lt;T&gt;()</c> are dispatched to SQS.
    ///
    /// Trains can also be marked with <c>[TraxRemote]</c> to opt into remote execution without
    /// explicit <c>ForTrain&lt;T&gt;()</c> routing. Builder routing takes precedence over the attribute.
    ///
    /// Jobs are sent as JSON messages containing a <see cref="RemoteJobRequest"/> payload.
    /// The consumer runs <c>JobRunnerTrain</c> to execute the train.
    ///
    /// Set up the consumer side with <see cref="Lambda.SqsJobRunnerHandler"/> for AWS Lambda,
    /// or use <c>AddTraxJobRunner()</c> with a custom SQS polling host.
    /// </remarks>
    /// <param name="builder">The scheduler configuration builder</param>
    /// <param name="configure">Action to configure the SQS queue URL and client options</param>
    /// <param name="routing">Action to specify which trains should be dispatched to SQS</param>
    /// <returns>The builder for method chaining</returns>
    public static SchedulerConfigurationBuilder UseSqsWorkers(
        this SchedulerConfigurationBuilder builder,
        Action<SqsWorkerOptions> configure,
        Action<SubmitterRouting> routing
    )
    {
        var options = new SqsWorkerOptions();
        configure(options);

        var submitterRouting = new SubmitterRouting();
        routing(submitterRouting);

        builder.AddRoutedSubmitter(
            new RoutedSubmitterRegistration(
                submitterRouting,
                typeof(SqsJobSubmitter),
                services =>
                {
                    services.AddSingleton(options);

                    services.AddSingleton<IAmazonSQS>(_ =>
                    {
                        var config = new AmazonSQSConfig();
                        options.ConfigureSqsClient?.Invoke(config);
                        return new AmazonSQSClient(config);
                    });

                    services.AddScoped<SqsJobSubmitter>();
                }
            )
        );

        return builder;
    }
}
