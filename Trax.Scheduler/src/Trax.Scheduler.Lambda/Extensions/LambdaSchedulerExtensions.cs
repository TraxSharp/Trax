using Amazon.Lambda;
using Microsoft.Extensions.DependencyInjection;
using Trax.Mediator.Services.RunExecutor;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Lambda.Configuration;
using Trax.Scheduler.Lambda.Services;
using Trax.Scheduler.Services.JobSubmitter;

namespace Trax.Scheduler.Lambda.Extensions;

/// <summary>
/// Extension methods for configuring AWS Lambda-based job dispatch on the scheduler.
/// </summary>
public static class LambdaSchedulerExtensions
{
    /// <summary>
    /// Routes specific trains to an AWS Lambda function for execution via direct SDK invocation.
    /// </summary>
    /// <remarks>
    /// Trains not included in the <paramref name="routing"/> configuration continue to execute
    /// locally via <c>PostgresJobSubmitter</c> and <c>LocalWorkerService</c>.
    /// Only the trains specified via <c>ForTrain&lt;T&gt;()</c> are dispatched to Lambda.
    ///
    /// Trains can also be marked with <c>[TraxRemote]</c> to opt into remote execution without
    /// explicit <c>ForTrain&lt;T&gt;()</c> routing. Builder routing takes precedence over the attribute.
    ///
    /// Jobs are sent as <see cref="Services.Lambda.LambdaEnvelope"/> payloads using
    /// <c>InvocationType.Event</c> (fire-and-forget). The Lambda function receives the payload
    /// via <c>TraxLambdaFunction</c> and executes the train.
    ///
    /// No public endpoint is created — access is governed by IAM policies. The calling service
    /// needs <c>lambda:InvokeFunction</c> permission on the target function.
    ///
    /// Set up the Lambda side with <c>TraxLambdaFunction</c> from <c>Trax.Runner.Lambda</c>.
    /// </remarks>
    /// <param name="builder">The scheduler configuration builder</param>
    /// <param name="configure">Action to configure the Lambda function name and client options</param>
    /// <param name="routing">Action to specify which trains should be dispatched to Lambda</param>
    /// <returns>The builder for method chaining</returns>
    public static SchedulerConfigurationBuilder UseLambdaWorkers(
        this SchedulerConfigurationBuilder builder,
        Action<LambdaWorkerOptions> configure,
        Action<SubmitterRouting>? routing = null
    )
    {
        var options = new LambdaWorkerOptions();
        configure(options);

        var submitterRouting = new SubmitterRouting();
        routing?.Invoke(submitterRouting);

        builder.AddRoutedSubmitter(
            new RoutedSubmitterRegistration(
                submitterRouting,
                typeof(LambdaJobSubmitter),
                services =>
                {
                    services.AddSingleton(options);

                    services.AddSingleton<IAmazonLambda>(_ =>
                    {
                        var config = new AmazonLambdaConfig();
                        options.ConfigureLambdaClient?.Invoke(config);
                        return new AmazonLambdaClient(config);
                    });

                    services.AddScoped<LambdaJobSubmitter>();
                }
            )
        );

        return builder;
    }

    /// <summary>
    /// Offloads synchronous run execution to an AWS Lambda function via direct SDK invocation.
    /// </summary>
    /// <remarks>
    /// Overrides the default <c>LocalRunExecutor</c> with <see cref="LambdaRunExecutor"/>.
    /// When a GraphQL <c>run*</c> mutation is called, the request is sent to the configured
    /// Lambda function using <c>InvocationType.RequestResponse</c> and blocks until the train
    /// completes. The Lambda function returns the serialized train output in the response payload.
    ///
    /// Without this, runs execute in-process via <c>LocalRunExecutor</c> (the default).
    ///
    /// No public endpoint is created — access is governed by IAM policies.
    ///
    /// Set up the Lambda side with <c>TraxLambdaFunction</c> from <c>Trax.Runner.Lambda</c>.
    /// </remarks>
    /// <param name="builder">The scheduler configuration builder</param>
    /// <param name="configure">Action to configure the Lambda function name and client options</param>
    /// <returns>The builder for method chaining</returns>
    public static SchedulerConfigurationBuilder UseLambdaRun(
        this SchedulerConfigurationBuilder builder,
        Action<LambdaRunOptions> configure
    )
    {
        builder.SetRemoteRunRegistration(services =>
        {
            var options = new LambdaRunOptions();
            configure(options);
            services.AddSingleton(options);

            services.AddSingleton<IAmazonLambda>(_ =>
            {
                var config = new AmazonLambdaConfig();
                options.ConfigureLambdaClient?.Invoke(config);
                return new AmazonLambdaClient(config);
            });

            services.AddScoped<IRunExecutor, LambdaRunExecutor>();
        });

        return builder;
    }
}
