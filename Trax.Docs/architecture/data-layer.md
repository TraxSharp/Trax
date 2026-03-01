---
layout: default
title: Data Layer
parent: Architecture
nav_order: 2
---

# Data Layer

## DataContext<TDbContext>

```csharp
public class DataContext<TDbContext> : DbContext, IDataContext
    where TDbContext : DbContext
{
    public DbSet<Metadata> Metadatas { get; set; }
    public DbSet<ManifestGroup> ManifestGroups { get; set; }
    public DbSet<Log> Logs { get; set; }

    // IEffectProvider implementation
    public async Task SaveChanges(CancellationToken cancellationToken)
    {
        await base.SaveChangesAsync(cancellationToken);
    }

    public async Task Track(IModel model)
    {
        Add(model);
    }

    // Transaction support
    public async Task<IDataContextTransaction> BeginTransaction() { }
    public async Task CommitTransaction() { }
    public async Task RollbackTransaction() { }
}
```

## Data Model Structure

```
┌──────────────────────────────────────────────────────────────────┐
│                       MANIFEST_GROUP                               │
├──────────────────────────────────────────────────────────────────┤
│ Id (PK)              │ int                                       │
│ Name                 │ string (unique)                           │
│ MaxActiveJobs        │ int?                                      │
│ Priority             │ smallint (0-31)                           │
│ IsEnabled            │ bool                                      │
│ CreatedAt            │ datetime                                  │
│ UpdatedAt            │ datetime                                  │
└──────────────────────────────────────────────────────────────────┘
       │
       │ 1:N
       ▼
┌──────────────────────────────────────────────────────────────────┐
│                           MANIFEST                                │
├──────────────────────────────────────────────────────────────────┤
│ Id (PK)              │ int                                       │
│ ExternalId           │ string                                    │
│ Name                 │ string                                    │
│ PropertyTypeName     │ string?                                   │
│ Properties           │ jsonb                                     │
│ IsEnabled            │ bool                                      │
│ ScheduleType         │ enum (None/Cron/Interval/Dependent)       │
│ CronExpression       │ string?                                   │
│ IntervalSeconds      │ int?                                      │
│ MaxRetries           │ int                                       │
│ TimeoutSeconds       │ int?                                      │
│ LastSuccessfulRun    │ datetime?                                 │
│ ManifestGroupId      │ int → ManifestGroup                       │
│ DependsOnManifestId  │ int? → Self                               │
└──────────────────────────────────────────────────────────────────┘
       │              │                  │
       │ 1:N          │ 1:N              │ 1:N
       ▼              ▼                  ▼
┌────────────┐  ┌────────────────┐  ┌─────────────────────────────┐
│ DEAD_LETTER│  │  WORK_QUEUE    │  │          METADATA            │
├────────────┤  ├────────────────┤  ├─────────────────────────────┤
│ Id     │int│  │ Id       │ int │  │ Id (PK)         │ int       │
│Manifest│int│  │ External │ str │  │ ParentId (FK)   │ int? →Self│
│  Id    │   │  │ Workflow │ str │  │ ManifestId (FK) │ int?      │
│DeadLet │ dt│  │   Name   │     │  │ ExternalId      │ string    │
│ teredAt│   │  │ Input    │json?│  │ Name            │ string    │
│ Status │enm│  │ InputTyp │str? │  │ Executor        │ string?   │
│ Reason │str│  │   eName  │     │  │ WorkflowState   │ enum      │
│RetryC. │int│  │ Status   │enum │  │ FailureStep     │ string?   │
│Resolved│dt?│  │ CreatedAt│ dt  │  │ FailureException│ string?   │
│Resoluti│   │  │Dispatched│ dt? │  │ FailureReason   │ string?   │
│  onNote│str│  │   At     │     │  │ StackTrace      │ string?   │
│RetryMet│   │  │ManifestId│int? │  │ Input           │ jsonb     │
│ adataId│int│  │MetadataId│int? │  │ Output          │ jsonb     │
└────────────┘  └────────────────┘  │ StartTime       │ datetime  │
                                    │ EndTime         │ datetime? │
  DeadLetterStatus:                 │ ScheduledTime   │ datetime? │
  - AwaitingIntervention            └─────────────────────────────┘
  - Retried                                  │
  - Acknowledged                             │ 1:N
                                             ▼
  WorkQueueStatus:              ┌─────────────────────────────────┐
  - Queued                      │              LOG                 │
  - Dispatched                  ├─────────────────────────────────┤
                                │ Id (PK)          │ int          │
                                │ MetadataId (FK)  │ int          │
                                │ EventId          │ int          │
                                │ Level            │ enum         │
                                │ Message          │ string       │
                                │ Category         │ string       │
                                │ Exception        │ string?      │
                                │ StackTrace       │ string?      │
                                └─────────────────────────────────┘
```

The **WorkQueue** table sits between scheduling and dispatch. When a manifest is due (or `TriggerAsync` is called), a `Queued` entry is created. The JobDispatcher reads these, creates a Metadata record, enqueues to the background task server, and flips the status to `Dispatched`. Both the Manifest and Metadata FKs use `ON DELETE RESTRICT`. The **ManifestGroup** table provides per-group dispatch controls. Every manifest belongs to exactly one group. Groups are auto-created during scheduling and orphaned groups (with no manifests) are cleaned up on startup.

The **BackgroundJob** table is a transient queue for the built-in PostgreSQL task server. When `JobDispatcherWorkflow` enqueues a job via `IBackgroundTaskServer.EnqueueAsync()`, a row is inserted. Worker threads claim jobs atomically using `FOR UPDATE SKIP LOCKED`, execute the workflow, and delete the row on completion. The `fetched_at` column enables crash recovery — if a worker dies mid-execution, the stale timestamp makes the job eligible for re-claim after the visibility timeout. See [Task Server]({{ site.baseurl }}{% link scheduler/task-server.md %}) for architecture details.

```
┌─────────────────────────────────────────────────────────────────┐
│                      BACKGROUND_JOB                               │
├─────────────────────────────────────────────────────────────────┤
│ Id (PK)           │ bigserial (auto-increment)                   │
│ MetadataId        │ int (NOT NULL)                               │
│ Input             │ jsonb (nullable)                              │
│ InputType         │ varchar(512) (nullable)                       │
│ CreatedAt         │ timestamptz (default: now())                  │
│ FetchedAt         │ timestamptz (nullable)                        │
└─────────────────────────────────────────────────────────────────┘

States (via FetchedAt):
- NULL       → Available for dequeue
- Recent     → Claimed by a worker (in progress)
- Stale      → Abandoned (eligible for re-claim after VisibilityTimeout)
- (deleted)  → Completed (row removed)
```

Additionally, **StepMetadata** tracks individual step executions within a workflow (not persisted to the database, used in-memory during workflow execution):

```
┌──────────────────────────────────────────────────────────────────┐
│                        STEP_METADATA                              │
├──────────────────────────────────────────────────────────────────┤
│ Id (PK)              │ int                                       │
│ WorkflowName         │ string                                    │
│ WorkflowExternalId   │ string                                    │
│ Name                 │ string                                    │
│ ExternalId           │ string                                    │
│ InputType            │ Type                                      │
│ OutputType           │ Type                                      │
│ State                │ EitherStatus                              │
│ HasRan               │ bool                                      │
│ StartTimeUtc         │ datetime?                                 │
│ EndTimeUtc           │ datetime?                                 │
│ OutputJson           │ string?                                   │
└──────────────────────────────────────────────────────────────────┘
```

## Implementation Variants

### PostgreSQL Implementation

```csharp
public class PostgresContext : DataContext<PostgresContext>
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder
            .HasDefaultSchema("trax")
            .AddPostgresEnums()                    // PostgreSQL enum types
            .ApplyUtcDateTimeConverter();          // UTC date handling
    }
}
```

**Features:**
- ACID transactions via PostgreSQL
- JSON column support for input/output parameters
- Automatic schema migration
- PostgreSQL-specific optimizations (enums, JSON queries)

### InMemory Implementation

```csharp
public class InMemoryContext : DataContext<InMemoryContext>
{
    // Minimal implementation - inherits all functionality
    // Uses Entity Framework Core's in-memory provider
}
```

**Features:**
- Fast, lightweight for testing
- No external dependencies
- Automatic cleanup between tests
- API-compatible with production database
