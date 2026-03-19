// ─────────────────────────────────────────────────────────────────────────────
// Trax Test Runner — Hub (GraphQL API + Scheduler + Local Workers)
//
// Runs NUnit test projects in-process via NUnit.Engine, orchestrated as Trax
// trains. Results stream to a React frontend via GraphQL subscriptions.
//
// GraphQL schema (auto-generated from train attributes):
//   Queries:    discoverTestProjects  — [TraxQuery]  (lists all test projects)
//   Mutations:  runTests(Queue)       — [TraxMutation(Queue)] [TraxBroadcast]
//   Subscriptions: onTrainCompleted, onTrainFailed (built-in via TraxBroadcast)
//
// Prerequisites:
//   1. Start Postgres:  cd Trax.Samples && docker compose up -d
//   2. Pack local:      ./pack-local.sh
//   3. Start Hub:       dotnet run --project samples/TestRunner/Trax.Samples.TestRunner.Hub
//   4. Start client:    cd samples/TestRunner/Trax.Samples.TestRunner.Client && npm run dev
//
// Endpoints:
//   Dashboard:   http://localhost:5220/trax
//   GraphQL IDE: http://localhost:5220/trax/graphql  (Banana Cake Pop)
//   React UI:    http://localhost:5173
//
// Try it:
//   # List all test projects
//   { discover { discoverTestProjects(input: {}) { projects { name repoName requiresPostgres } } } }
//
//   # Queue a test run
//   mutation { dispatch { runTests(input: { projectName: "Trax.Core.Tests.Unit", projectPath: "/path/to/Trax.Core.Tests.Unit.csproj" }) { externalId workQueueId } } }
//
//   # Subscribe to results
//   subscription { onTrainCompleted { externalId trainName output } }
// ─────────────────────────────────────────────────────────────────────────────

using Trax.Api.Extensions;
using Trax.Api.GraphQL.Extensions;
using Trax.Dashboard.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.TestRunner.Services;
using Trax.Scheduler.Extensions;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

builder.Services.AddLogging(logging => logging.AddConsole());
builder.Services.AddSingleton<TestProjectRegistry>();

// ── Register Trax Effect + Mediator + Scheduler ──────────────────────────────
builder.Services.AddTrax(trax =>
    trax.AddEffects(effects =>
            effects
                .UsePostgres(connectionString)
                .AddJson()
                .SaveTrainParameters()
                .AddJunctionProgress()
        )
        .AddMediator(typeof(TestProjectRegistry).Assembly)
        .AddScheduler(scheduler =>
            scheduler
                .PollingInterval(TimeSpan.FromSeconds(1))
                .ConfigureLocalWorkers(w => w.WorkerCount = 1)
                .DefaultJobTimeout(TimeSpan.FromMinutes(30))
        )
);

// ── Register GraphQL API ─────────────────────────────────────────────────────
// Trains annotated with [TraxQuery] or [TraxMutation] get typed GraphQL
// fields auto-generated. [TraxBroadcast] trains emit subscription events.
builder.Services.AddAuthorization();
builder.AddTraxDashboard();
builder.Services.AddTraxGraphQL();
builder.Services.AddHealthChecks().AddTraxHealthCheck();

// ── CORS — allow React dev server ────────────────────────────────────────────
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
    )
);

var app = builder.Build();

app.UseCors();
app.UseTraxDashboard();
app.UseTraxGraphQL();
app.MapHealthChecks("/trax/health");

app.Run();
