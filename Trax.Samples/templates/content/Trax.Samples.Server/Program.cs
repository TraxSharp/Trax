using Trax.Dashboard.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Workflows.ManifestManager;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Samples.Server.Workflows.HelloWorld;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Trax.CoreDatabase")
    ?? throw new InvalidOperationException("Connection string 'Trax.CoreDatabase' not found.");

builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Information);
});

builder.AddTrax.CoreDashboard();

builder.Services.AddTrax.CoreEffects(
    options =>
        options
            .AddEffectWorkflowBus(
                assemblies: [typeof(Program).Assembly, typeof(ManifestManagerWorkflow).Assembly,]
            )
            .AddPostgresEffect(connectionString)
            .AddJsonEffect()
            .SaveWorkflowParameters()
            .AddScheduler(scheduler =>
            {
                scheduler
                    .AddMetadataCleanup(cleanup =>
                    {
                        cleanup.AddWorkflowType<IHelloWorldWorkflow>();
                    })
                    .UseHangfire(connectionString)
                    .Schedule<IHelloWorldWorkflow>(
                        "hello-world",
                        new HelloWorldInput { Name = "Trax.Core" },
                        Every.Seconds(20)
                    );
            })
);

var app = builder.Build();

app.UseTrax.CoreDashboard();
app.UseHangfireDashboard("/hangfire", new DashboardOptions { Authorization = [] });

app.Run();
