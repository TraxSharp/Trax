// ─────────────────────────────────────────────────────────────────────────────
// ContentShield — API (GraphQL + Dashboard, ephemeral HTTP dispatch)
//
// A single process that serves the GraphQL API and hosts the Trax dashboard.
// There are NO scheduled jobs — all work is triggered by GraphQL mutations.
// Queued mutations are dispatched via HTTP to the ephemeral Runner process
// using UseRemoteWorkers(). The Runner simulates a Lambda/serverless function.
//
// This demonstrates the Ephemeral Workers pattern:
//   - Query trains (LookupModerationResult) run synchronously on this process
//   - Queued trains (ReviewContent, SendViolationNotice, GenerateModerationReport)
//     are POSTed to the Runner via HTTP — no background_job table, no DB polling
//   - Run mutations (GenerateModerationReport) are also offloaded to the Runner
//     via UseRemoteRun() — the API blocks until the Runner returns the output
//
// GraphQL schema (auto-generated from train attributes):
//   Queries:    lookupModerationResult                — [TraxQuery]
//   Mutations:  queueReviewContent                    — [TraxMutation(Queue)]
//               queueSendViolationNotice              — [TraxMutation(Queue)]
//               runGenerateModerationReport           — [TraxMutation(RunAndQueue)]
//               queueGenerateModerationReport         — [TraxMutation(RunAndQueue)]
//   Subscriptions: onTrainStarted, onTrainCompleted, onTrainFailed
//
// Prerequisites:
//   1. Start Postgres:  cd Trax.Samples && docker compose up -d
//   2. Pack local:      ./pack-local.sh
//   3. Start runner:    dotnet run --project samples/EphemeralWorkers/Trax.Samples.ContentShield.Runner
//   4. Start API:       dotnet run --project samples/EphemeralWorkers/Trax.Samples.ContentShield.Api
//
// Endpoints:
//   Dashboard:   http://localhost:5204/trax
//   GraphQL IDE: http://localhost:5204/trax/graphql  (Banana Cake Pop)
//
// Try it:
//   # Look up a moderation result (runs on API)
//   curl -X POST http://localhost:5204/trax/graphql \
//        -H "Content-Type: application/json" \
//        -d '{"query":"{ discover { lookupModerationResult(input: {contentId: \"test-001\"}) { contentId moderationStatus classification threatScore } } }"}'
//
//   # Queue a content review (dispatched to Runner via HTTP)
//   curl -X POST http://localhost:5204/trax/graphql \
//        -H "Content-Type: application/json" \
//        -d '{"query":"mutation { dispatch { queueReviewContent(input: {contentId: \"test-002\", contentType: \"video\", contentBody: \"suspicious video content\"}) { workQueueId externalId } } }"}'
//
//   # Generate a moderation report (offloaded to Runner via UseRemoteRun, blocks until done)
//   curl -X POST http://localhost:5204/trax/graphql \
//        -H "Content-Type: application/json" \
//        -d '{"query":"mutation { dispatch { runGenerateModerationReport(input: {reportPeriod: \"Daily\"}) { totalReviewed totalFlagged topViolationTypes falsePositiveRate } } }"}'
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Dashboard.Extensions;
using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.StepProvider.Progress.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.ContentShield.Trains.ContentReview.ReviewContent;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

var rabbitMqConnectionString =
    builder.Configuration.GetConnectionString("RabbitMQ")
    ?? throw new InvalidOperationException("Connection string 'RabbitMQ' not found.");

builder.Services.AddLogging(logging => logging.AddConsole());

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .AddDataContextLogging()
                .AddJson()
                .SaveTrainParameters()
                .AddStepProgress()
                .UseBroadcaster(b => b.UseRabbitMq(rabbitMqConnectionString))
        )
        .AddMediator(typeof(ReviewContentTrain).Assembly)
        .AddScheduler(scheduler =>
        {
            // ── Ephemeral dispatch only — no scheduled jobs ──────────────────
            // UseRemoteWorkers replaces the default PostgresJobSubmitter with
            // HttpJobSubmitter. When a GraphQL queue* mutation is called, the
            // JobDispatcher POSTs the job directly to the Runner via HTTP.
            // No cron schedules, no intervals, no manifests — purely on-demand.
            scheduler.UseRemoteWorkers(remote =>
                remote.BaseUrl = "http://localhost:5205/trax/execute"
            );

            // ── Remote run offloading ────────────────────────────────────────
            // UseRemoteRun replaces the default LocalRunExecutor with
            // HttpRunExecutor. When a GraphQL run* mutation is called, the
            // request is POSTed to the Runner and blocks until the train
            // completes. Without this, runs execute in-process on this API.
            scheduler.UseRemoteRun(remote => remote.BaseUrl = "http://localhost:5205/trax/run");
        })
);

// ── Register GraphQL API ────────────────────────────────────────────────
// Trains annotated with [TraxQuery] or [TraxMutation] get typed GraphQL
// fields auto-generated. [TraxBroadcast] trains emit subscription events.
builder.AddTraxDashboard();
builder.Services.AddTraxGraphQL();
builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

app.UseTraxDashboard();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

app.Run();
