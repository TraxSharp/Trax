// ─────────────────────────────────────────────────────────────────────────────
// ContentShield: API (GraphQL + dashboard) dispatching to an ephemeral runner
//
// No scheduled jobs: every piece of work starts with a GraphQL request, and the
// API executes none of it. Queued mutations are POSTed to the Runner over HTTP
// (UseRemoteWorkers) and return at once. Every synchronous run, which means run
// mutations AND [TraxQuery] queries, is POSTed too and waits for the answer
// (UseRemoteRun replaces the in-process run executor). No background_job table
// is involved. The Runner is a TraxLambdaFunction: in production AWS invokes it;
// locally RunLocalAsync serves the same /trax/execute and /trax/run routes.
//
// Every request to the Runner is signed with a key both sides share
// (Trax:RunnerSigningKey, the base64 of 32+ random bytes). In Development a
// published demo key is used when none is configured; anywhere else a missing
// key stops startup.
//
// For AWS, swap UseRemoteWorkers/UseRemoteRun for UseLambdaWorkers/UseLambdaRun
// (package Trax.Scheduler.Lambda), which invoke the function through the AWS SDK
// with no public endpoint:
//
//   using Trax.Scheduler.Lambda.Extensions;
//
//   .UseLambdaWorkers(
//       lambda => { lambda.FunctionName = "content-shield-runner"; lambda.SigningKey = runnerKey; },
//       routing => routing
//           .ForTrain<IReviewContentTrain>()
//           .ForTrain<ISendViolationNoticeTrain>()
//           .ForTrain<IGenerateModerationReportTrain>())
//   .UseLambdaRun(lambda => { lambda.FunctionName = "content-shield-runner"; lambda.SigningKey = runnerKey; })
//
// GraphQL schema (generated from the train attributes):
//   Queries:    discover { moderation { lookupModerationResult } }          anonymous, run on the Runner
//   Mutations:  dispatch { moderation { reviewContent } }                   anonymous, queued to the Runner
//               dispatch { sendViolationNotice }                            Moderator, queued to the Runner
//               dispatch { reports { generateModerationReport(mode:) } }    Moderator, run or queued on the Runner
//   Subscriptions: onTrainStarted, onTrainCompleted, onTrainFailed, ... for the Moderator
//                  trains only; the anonymous ones are not broadcast
//
// Run it (from Trax.Samples/):
//   1. docker compose up -d        Postgres on 5432, RabbitMQ on 5672 (user trax / trax123)
//   2. dotnet run --project samples/EphemeralWorkers/Trax.Samples.ContentShield.Runner
//   3. dotnet run --project samples/EphemeralWorkers/Trax.Samples.ContentShield.Api
//
// Endpoints (Development):
//   Dashboard:   http://localhost:5204/trax
//   GraphQL IDE: http://localhost:5204/trax/graphql
//
// Try it:
//   # Look up a moderation result (anonymous; the API waits while the Runner runs it)
//   curl -s http://localhost:5204/trax/graphql -H "Content-Type: application/json" \
//     -d '{"query":"{ discover { moderation { lookupModerationResult(input: {contentId: \"test-001\"}) { contentId moderationStatus classification threatScore } } } }"}'
//
//   # Queue a content review; the Runner's console logs it (anonymous)
//   curl -s http://localhost:5204/trax/graphql -H "Content-Type: application/json" \
//     -d '{"query":"mutation { dispatch { moderation { reviewContent(input: {contentId: \"test-002\", contentType: \"video\", contentBody: \"suspicious video content\"}) { externalId workQueueId } } } }"}'
//
//   # Run a moderation report on the Runner and wait for it (Moderator key, Development only)
//   curl -s http://localhost:5204/trax/graphql -H "Content-Type: application/json" \
//     -H "X-Api-Key: contentshield-moderator-key-do-not-use-in-production" \
//     -d '{"query":"mutation { dispatch { reports { generateModerationReport(input: {reportPeriod: \"Daily\"}) { externalId output { totalReviewed totalFlagged topViolationTypes falsePositiveRate } } } } }"}'
//
// Docs: https://traxsharp.net/docs/samples/content-shield
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Dashboard.Extensions;
using Trax.Effect.Broadcaster.RabbitMQ.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.ContentShield;
using Trax.Samples.ContentShield.Api;
using Trax.Samples.ContentShield.Trains.ContentReview.ReviewContent;
using Trax.Samples.ContentShield.Trains.Notices.SendViolationNotice;
using Trax.Samples.ContentShield.Trains.Reports.GenerateModerationReport;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

var rabbitMqConnectionString =
    builder.Configuration.GetConnectionString("RabbitMQ")
    ?? throw new InvalidOperationException("Connection string 'RabbitMQ' not found.");

// Where the Runner listens. RunLocalAsync serves /trax/execute and /trax/run on it.
var runnerBaseUrl = builder.Configuration["Runner:BaseUrl"] ?? "http://localhost:5205";
var runnerKey = RunnerSigningKey.Resolve(
    builder.Configuration,
    builder.Environment.IsDevelopment()
);

builder.Services.AddLogging(logging => logging.AddConsole());

// ── Who may moderate ────────────────────────────────────────────────────
// sendViolationNotice and generateModerationReport require the Moderator role. The only key that
// carries it is a published demo key, registered in Development alone.
if (builder.Environment.IsDevelopment())
    builder.Services.AddTraxApiKeyAuth(keys =>
        keys.Add(DemoKeys.ModeratorKey, id: "moderator", ContentShieldRoles.Moderator)
    );
builder.Services.AddAuthentication();
builder.Services.AddAuthorization();

builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .AddDataContextLogging()
                .UseBroadcaster(b =>
                    b.UseRabbitMq(
                        rabbitMqConnectionString,
                        o => o.ExchangeName = LifecycleEvents.ExchangeName
                    )
                )
        )
        .AddMediator(mediator =>
            mediator
                .ScanAssemblies(typeof(ReviewContentTrain).Assembly)
                .GlobalConcurrentRunLimit(25)
        )
        .AddScheduler(scheduler =>
            scheduler
                // ── Ephemeral dispatch only — no scheduled jobs ──────────────────
                // UseRemoteWorkers routes the specified trains to HttpJobSubmitter.
                // When a GraphQL mutation is called with mode: QUEUE for a routed train,
                // the JobDispatcher POSTs the job directly to the Runner via HTTP.
                // No cron schedules, no intervals, no manifests — purely on-demand.
                .UseRemoteWorkers(
                    remote =>
                    {
                        remote.BaseUrl = $"{runnerBaseUrl}/trax/execute";
                        remote.SigningKey = runnerKey;
                    },
                    routing =>
                        routing
                            .ForTrain<IReviewContentTrain>()
                            .ForTrain<ISendViolationNoticeTrain>()
                            .ForTrain<IGenerateModerationReportTrain>()
                )
                // ── Remote run offloading ────────────────────────────────────────
                // UseRemoteRun replaces the in-process run executor. Every
                // synchronous run (a mutation in RUN mode, and every [TraxQuery]) is
                // POSTed to the Runner and waits until the train completes. Without
                // this, runs execute in-process on this API.
                .UseRemoteRun(remote =>
                {
                    remote.BaseUrl = $"{runnerBaseUrl}/trax/run";
                    remote.SigningKey = runnerKey;
                })
        )
);

// ── Dashboard: Development only ────────────────────────────────────────
// The dashboard can queue, run and cancel trains. This sample puts no login in front of it, so it
// is served only in Development. Gate it (RequirePolicy / RequireRoles) before serving it elsewhere.
if (builder.Environment.IsDevelopment())
    builder.AddTraxDashboard(dashboard => dashboard.AllowAnonymousDashboard());

// ── Register GraphQL API ────────────────────────────────────────────────
// Trains annotated with [TraxQuery] or [TraxMutation] get typed GraphQL
// fields generated. [TraxBroadcast] trains emit subscription events, and the
// Runner's events reach them over RabbitMQ.
builder.Services.AddTraxGraphQL();
builder.Services.AddHealthChecks().AddTraxHealthCheck();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
if (app.Environment.IsDevelopment())
    app.UseTraxDashboard();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

app.Run();

namespace Trax.Samples.ContentShield.Api
{
    public partial class Program;
}
