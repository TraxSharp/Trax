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
    /// Dispatches jobs to an SQS queue for execution by Lambda or another SQS consumer.
    /// </summary>
    /// <remarks>
    /// Overrides the default <see cref="PostgresJobSubmitter"/> via <see cref="SchedulerConfigurationBuilder.OverrideSubmitter"/>.
    /// Jobs are sent as JSON messages containing a <see cref="RemoteJobRequest"/> payload.
    /// The consumer runs <c>JobRunnerTrain</c> to execute the train.
    ///
    /// Set up the consumer side with <see cref="Lambda.SqsJobRunnerHandler"/> for AWS Lambda,
    /// or use <c>AddTraxJobRunner()</c> with a custom SQS polling host.
    /// </remarks>
    /// <param name="builder">The scheduler configuration builder</param>
    /// <param name="configure">Action to configure the SQS queue URL and client options</param>
    /// <returns>The builder for method chaining</returns>
    public static SchedulerConfigurationBuilder UseSqsWorkers(
        this SchedulerConfigurationBuilder builder,
        Action<SqsWorkerOptions> configure
    )
    {
        builder.OverrideSubmitter(services =>
        {
            var options = new SqsWorkerOptions();
            configure(options);
            services.AddSingleton(options);

            services.AddSingleton<IAmazonSQS>(_ =>
            {
                var config = new AmazonSQSConfig();
                options.ConfigureSqsClient?.Invoke(config);
                return new AmazonSQSClient(config);
            });

            services.AddScoped<IJobSubmitter, SqsJobSubmitter>();
        });

        return builder;
    }
}
