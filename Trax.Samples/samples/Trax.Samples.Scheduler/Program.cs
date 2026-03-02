using Trax.Dashboard.Extensions;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.StepProvider.Progress.Extensions;
using Trax.Mediator.Extensions;
using Trax.Samples.Scheduler;
using Trax.Samples.Scheduler.Trains.AlwaysFails;
using Trax.Samples.Scheduler.Trains.DataQualityCheck;
using Trax.Samples.Scheduler.Trains.ExtractImport;
using Trax.Samples.Scheduler.Trains.GoodbyeWorld;
using Trax.Samples.Scheduler.Trains.HelloWorld;
using Trax.Samples.Scheduler.Trains.TransformLoad;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Trains.ManifestManager;

var builder = WebApplication.CreateBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("TraxDatabase")
    ?? throw new InvalidOperationException("Connection string 'TraxDatabase' not found.");

builder.Services.AddLogging(logging => logging.AddConsole());
builder.AddTraxDashboard();

builder.Services.AddTraxEffects(options =>
    options
        .AddServiceTrainBus(
            assemblies: [typeof(Program).Assembly, typeof(ManifestManagerTrain).Assembly]
        )
        .AddPostgresEffect(connectionString)
        .AddEffectDataContextLogging()
        .AddJsonEffect()
        .SaveTrainParameters()
        .AddStepProgress()
        .AddScheduler(scheduler =>
        {
            // ── Global Configuration ──────────────────────────────────────────────
            scheduler
                .AddMetadataCleanup(cleanup =>
                {
                    cleanup.AddTrainType<IHelloWorldTrain>();
                    cleanup.AddTrainType<IGoodbyeWorldTrain>();
                    cleanup.AddTrainType<IExtractImportTrain>();
                    cleanup.AddTrainType<ITransformLoadTrain>();
                    cleanup.AddTrainType<IDataQualityCheckTrain>();
                    cleanup.AddTrainType<IAlwaysFailsTrain>();
                })
                .JobDispatcherPollingInterval(TimeSpan.FromSeconds(2))
                .UsePostgresTaskServer();

            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            // 1. SIMPLE RECURRING SCHEDULE
            //    Schedule() registers a single train on a recurring timer.
            //    Every.* helpers create interval-based schedules.
            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            scheduler.Schedule<IHelloWorldTrain>(
                ManifestNames.HelloWorld,
                new HelloWorldInput { Name = "Trax.Core" },
                Every.Seconds(20)
            );

            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            // 2. CRON-BASED SCHEDULE
            //    Cron.* helpers: Minutely(), Hourly(), Daily(), Weekly(),
            //    Monthly(), Expression("0 */6 * * *")
            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            scheduler.Schedule<IGoodbyeWorldTrain>(
                ManifestNames.GoodbyeNightly,
                new GoodbyeWorldInput { Name = "Night Shift" },
                Cron.Daily(hour: 3)
            );

            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            // 3. RETRY POLICY & DEAD LETTERS
            //    MaxRetries(N) limits retry attempts before dead-lettering.
            //    This train always throws, so it dead-letters after 1 attempt.
            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            scheduler.Schedule<IAlwaysFailsTrain>(
                ManifestNames.AlwaysFails,
                new AlwaysFailsInput { Scenario = "Database connection timeout" },
                Every.Seconds(30),
                o => o.MaxRetries(1)
            );

            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            // 4. DEPENDENCY TOPOLOGY
            //    Include()      → fan-out from the root Schedule
            //    ThenInclude()  → chain from the most recently declared manifest
            //    IncludeMany()  → batch fan-out from the root (no DependsOn needed)
            //
            //    hello-greeter (root, every 2 min)
            //      ├── farewell-a      (Include  — depends on root)
            //      ├── farewell-b      (Include  — depends on root)
            //      │   └── farewell-c  (ThenInclude — depends on farewell-b)
            //      └── broadcast-{0…4} (IncludeMany — all depend on root)
            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            scheduler
                .Schedule<IHelloWorldTrain>(
                    ManifestNames.HelloGreeter,
                    new HelloWorldInput { Name = "Greeter Pipeline" },
                    Every.Minutes(2)
                )
                .Include<IGoodbyeWorldTrain>(
                    ManifestNames.FarewellA,
                    new GoodbyeWorldInput { Name = "Branch A" }
                )
                .Include<IGoodbyeWorldTrain>(
                    ManifestNames.FarewellB,
                    new GoodbyeWorldInput { Name = "Branch B" }
                )
                .ThenInclude<IGoodbyeWorldTrain>(
                    ManifestNames.FarewellC,
                    new GoodbyeWorldInput { Name = "Chained after B" }
                )
                .IncludeMany<IGoodbyeWorldTrain>(
                    ManifestNames.Broadcast,
                    Enumerable
                        .Range(0, 5)
                        .Select(i => new ManifestItem(
                            $"{i}",
                            new GoodbyeWorldInput { Name = $"Recipient {i}" }
                        ))
                );

            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            // 5. ETL PIPELINE: ScheduleMany → IncludeMany → ThenIncludeMany
            //    ScheduleMany()     — registers N manifests in one transaction.
            //                         Name-based IDs: "{name}-{item.Id}".
            //    IncludeMany()      — batch dependents with explicit DependsOn per item.
            //    ThenIncludeMany()  — batch chain from the previous IncludeMany cursor.
            //
            //    extract-customer-{i} (every 5 min)
            //      └── transform-customer-{i} (IncludeMany, DependsOn each extract)
            //          └── dq-customer-{i}    (ThenIncludeMany, DependsOn each transform)
            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            scheduler
                .ScheduleMany<IExtractImportTrain>(
                    ManifestNames.ExtractCustomer,
                    Enumerable
                        .Range(0, 10)
                        .Select(i => new ManifestItem(
                            $"{i}",
                            new ExtractImportInput
                            {
                                TableName = ManifestNames.CustomerTable,
                                Index = i,
                            }
                        )),
                    Every.Minutes(5)
                )
                .IncludeMany<ITransformLoadTrain>(
                    ManifestNames.TransformCustomer,
                    Enumerable
                        .Range(0, 10)
                        .Select(i => new ManifestItem(
                            $"{i}",
                            new TransformLoadInput
                            {
                                TableName = ManifestNames.CustomerTable,
                                Index = i,
                            },
                            DependsOn: ManifestNames.WithIndex(ManifestNames.ExtractCustomer, i)
                        ))
                )
                .ThenIncludeMany<IDataQualityCheckTrain>(
                    ManifestNames.DqCustomer,
                    Enumerable
                        .Range(0, 10)
                        .Select(i => new ManifestItem(
                            $"{i}",
                            new DataQualityCheckInput
                            {
                                TableName = ManifestNames.CustomerTable,
                                Index = i,
                                AnomalyCount = 0,
                            },
                            DependsOn: ManifestNames.WithIndex(ManifestNames.TransformCustomer, i)
                        ))
                );

            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            // 6. DORMANT DEPENDENTS & GROUP CONCURRENCY
            //    Dormant()             — dependents that never auto-fire; activated
            //                            at runtime via IDormantDependentContext.
            //    Priority(N)           — dispatch order (0–31, higher = first).
            //    Group(MaxActiveJobs)  — caps concurrent jobs within the batch.
            //
            //    extract-transaction-{i} (every 5 min, priority 24, max 10 concurrent)
            //      └── dq-transaction-{i} (Dormant — activated only when anomalies found)
            // ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
            scheduler
                .ScheduleMany<IExtractImportTrain>(
                    ManifestNames.ExtractTransaction,
                    Enumerable
                        .Range(0, 30)
                        .Select(i => new ManifestItem(
                            $"{i}",
                            new ExtractImportInput
                            {
                                TableName = ManifestNames.TransactionTable,
                                Index = i,
                            }
                        )),
                    Every.Minutes(5),
                    o => o.Priority(24).Group(group => group.MaxActiveJobs(10))
                )
                .IncludeMany<IDataQualityCheckTrain>(
                    ManifestNames.DqTransaction,
                    Enumerable
                        .Range(0, 30)
                        .Select(i => new ManifestItem(
                            $"{i}",
                            new DataQualityCheckInput
                            {
                                TableName = ManifestNames.TransactionTable,
                                Index = i,
                                AnomalyCount = 0,
                            },
                            DependsOn: ManifestNames.WithIndex(ManifestNames.ExtractTransaction, i)
                        )),
                    options: o => o.Dormant()
                );
        })
);

var app = builder.Build();

app.UseTraxDashboard();

app.Run();
