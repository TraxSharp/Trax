// ─────────────────────────────────────────────────────────────────────────────
// Trax.Core Scheduler × Flowthru Spaceflights Sample
//
// Demonstrates Trax.Core's scheduler orchestrating three Flowthru data
// pipelines as a linear dependency chain:
//
//   data-processing → data-science → reporting
//
// Pipeline logic, data catalog, and example datasets are from the
// KedroSpaceflights.Pure example in the Flowthru project by @Spelkington:
//   https://github.com/chaoticgoodcomputing/flowthru
//
// Original dataset: Kedro Spaceflights tutorial (Apache 2.0)
//   https://github.com/kedro-org/kedro-starters
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Dashboard.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Workflows.ManifestManager;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.StepProvider.Progress.Extensions;
using Trax.Samples.Flowthru.Spaceflights;
using Trax.Samples.Flowthru.Spaceflights.Workflows.DataProcessing;
using Trax.Samples.Flowthru.Spaceflights.Workflows.DataScience;
using Trax.Samples.Flowthru.Spaceflights.Workflows.Reporting;
using KedroSpaceflights.Pure.Data;
using KedroSpaceflights.Pure.Pipelines.DataProcessing;
using KedroSpaceflights.Pure.Pipelines.DataScience;
using KedroSpaceflights.Pure.Pipelines.Reporting;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("Trax.CoreDatabase")
    ?? throw new InvalidOperationException("Connection string 'Trax.CoreDatabase' not found.");

builder.Services.AddLogging(logging => logging.AddConsole());
builder.AddTrax.CoreDashboard();

// ── Register Flowthru services ──────────────────────────────────────────────
// Pipeline logic by @Spelkington — https://github.com/chaoticgoodcomputing/flowthru
builder.Services.AddFlowthru(flowthru =>
{
    flowthru.UseConfiguration();
    flowthru.UseCatalog(_ => new Catalog("Data"));

    flowthru
        .RegisterPipeline<Catalog>(label: "DataProcessing", pipeline: DataProcessingPipeline.Create)
        .WithDescription("Preprocesses companies and shuttles data");

    flowthru
        .RegisterPipelineWithConfiguration<Catalog, DataSciencePipeline.Params>(
            label: "DataScience",
            pipeline: DataSciencePipeline.Create,
            configurationSection: "Flowthru:Pipelines:DataScience"
        )
        .WithDescription("Trains linear regression model for price prediction");

    flowthru
        .RegisterPipelineWithConfiguration<Catalog, ReportingPipeline.Params>(
            label: "Reporting",
            pipeline: ReportingPipeline.Create,
            configurationSection: "Flowthru:Pipelines:Reporting"
        )
        .WithDescription("Generates passenger capacity reports and visualizations");
});

// ── Register Trax.Core Effect + Scheduler ──────────────────────────────────
builder.Services.AddTrax.CoreEffects(
    options =>
        options
            .AddServiceTrainBus(
                assemblies: [typeof(Program).Assembly, typeof(ManifestManagerWorkflow).Assembly]
            )
            .AddPostgresEffect(connectionString)
            .AddEffectDataContextLogging()
            .AddJsonEffect()
            .SaveWorkflowParameters()
            .AddStepProgress()
            .AddScheduler(scheduler =>
            {
                scheduler
                    .AddMetadataCleanup(cleanup =>
                    {
                        cleanup.AddWorkflowType<IDataProcessingPipelineWorkflow>();
                        cleanup.AddWorkflowType<IDataSciencePipelineWorkflow>();
                        cleanup.AddWorkflowType<IReportingPipelineWorkflow>();
                    })
                    .JobDispatcherPollingInterval(TimeSpan.FromSeconds(2))
                    .UsePostgresTaskServer();

                // ── Spaceflights Pipeline Topology ──────────────────────────────
                //    data-processing (root, every 5 min)
                //      └── data-science   (ThenInclude — depends on data-processing)
                //          └── reporting   (ThenInclude — depends on data-science)
                scheduler
                    .Schedule<IDataProcessingPipelineWorkflow>(
                        ManifestNames.DataProcessing,
                        new DataProcessingPipelineInput(),
                        Every.Minutes(5)
                    )
                    .ThenInclude<IDataSciencePipelineWorkflow>(
                        ManifestNames.DataScience,
                        new DataSciencePipelineInput()
                    )
                    .ThenInclude<IReportingPipelineWorkflow>(
                        ManifestNames.Reporting,
                        new ReportingPipelineInput()
                    );
            })
);

var app = builder.Build();

app.UseTrax.CoreDashboard();

app.Run();
