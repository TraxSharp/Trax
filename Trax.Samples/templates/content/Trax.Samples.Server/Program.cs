using Trax.Dashboard.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.Server.Trains.HelloWorld;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Trains.ManifestManager;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(LogLevel.Information);
});

builder.AddTraxDashboard();

builder.Services.AddTraxEffects(options =>
    options
        .AddServiceTrainBus(
            assemblies: [typeof(Program).Assembly, typeof(ManifestManagerTrain).Assembly]
        )
        .AddPostgresEffect(connectionString)
        .AddJsonEffect()
        .SaveTrainParameters()
        .AddScheduler(scheduler =>
        {
            scheduler
                .AddMetadataCleanup(cleanup =>
                {
                    cleanup.AddTrainType<IHelloWorldTrain>();
                })
                .UseHangfire(connectionString)
                .Schedule<IHelloWorldTrain>(
                    "hello-world",
                    new HelloWorldInput { Name = "Trax.Core" },
                    Every.Seconds(20)
                );
        })
);

var app = builder.Build();

app.UseTraxDashboard();
app.UseHangfireDashboard("/hangfire", new DashboardOptions { Authorization = [] });

app.Run();
