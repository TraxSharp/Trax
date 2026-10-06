using Amazon.SQS;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    /// A scheduler with a <c>[TraxRemote]</c> train and no remote submitter at all (this,
    /// <c>UseRemoteWorkers</c>, <c>UseSqsWorkers</c> or <c>UseLambdaWorkers</c>) refuses to build,
    /// rather than run the train locally.
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
        Action<SubmitterRouting>? routing = null
    )
    {
        var options = new SqsWorkerOptions();
        configure(options);
        if (options.SigningKey is not null)
            Trax.Scheduler.Services.RequestSigning.RunnerRequestSignature.EnsureKey(
                options.SigningKey,
                nameof(SqsWorkerOptions.SigningKey)
            );

        var submitterRouting = new SubmitterRouting();
        routing?.Invoke(submitterRouting);

        // Each call keeps its own options and its own client, so a second call for another
        // queue neither takes over the first one's trains nor shares its settings.
        var clientKey = $"Trax.SqsWorkers.{Guid.NewGuid():N}";

        builder.AddRoutedSubmitter(
            new RoutedSubmitterRegistration(
                submitterRouting,
                typeof(SqsJobSubmitter),
                services =>
                {
                    services.AddKeyedSingleton<IAmazonSQS>(
                        clientKey,
                        (_, _) =>
                        {
                            var config = new AmazonSQSConfig();
                            options.ConfigureSqsClient?.Invoke(config);
                            return new AmazonSQSClient(config);
                        }
                    );

                    // The first call's options and submitter stay resolvable by type, as they
                    // were before routing was per registration.
                    services.TryAddSingleton(options);
                    services.TryAddSingleton<IAmazonSQS>(sp =>
                        sp.GetRequiredKeyedService<IAmazonSQS>(clientKey)
                    );
                    services.TryAddScoped<SqsJobSubmitter>();
                }
            )
            {
                CreateSubmitter = services => new SqsJobSubmitter(
                    services.GetRequiredKeyedService<IAmazonSQS>(clientKey),
                    options
                ),
                Description = $"UseSqsWorkers({options.QueueUrl})",
            }
        );

        return builder;
    }
}
