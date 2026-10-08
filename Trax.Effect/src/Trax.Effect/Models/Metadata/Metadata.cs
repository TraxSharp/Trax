using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Host;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Utils;

namespace Trax.Effect.Models.Metadata;

/// <summary>
/// Represents the metadata for a train execution in the Trax.Effect system.
/// This class implements the IMetadata interface and provides the concrete implementation
/// for tracking train execution details.
/// </summary>
/// <remarks>
/// The Metadata class is the central entity for train tracking in the system.
/// It stores comprehensive information about train executions, including:
///
/// 1. Identification and relationships (Id, ParentId, ExternalId)
/// 2. Basic train information (Name, Executor)
/// 3. State and timing (TrainState, StartTime, EndTime)
/// 4. Input and output data (Input, Output, InputObject, OutputObject)
/// 5. Error information (FailureJunction, FailureException, FailureReason, StackTrace)
/// 6. Relationships to other entities (Parent, Children, Logs)
///
/// This class is designed to be persisted to a database and serves as the
/// primary record of train execution in the system.
///
/// IMPORTANT: This class implements IDisposable to properly dispose of JsonDocument objects
/// that hold unmanaged memory resources.
/// </remarks>
public class Metadata : IModel, IDisposable
{
    #region Columns

    /// <summary>
    /// Gets or sets the unique identifier for this metadata record.
    /// </summary>
    /// <remarks>
    /// This is the primary key in the database and is automatically generated
    /// when the record is persisted.
    /// </remarks>
    [Column("id")]
    [JsonPropertyName("id")]
    public long Id { get; private set; }

    /// <summary>
    /// Gets or sets the identifier of the parent train, if this train
    /// was triggered by another train.
    /// </summary>
    /// <remarks>
    /// This property establishes parent-child relationships between trains,
    /// enabling hierarchical tracking of complex train compositions.
    /// </remarks>
    [Column("parent_id")]
    [JsonPropertyName("parent_id")]
    [JsonInclude]
    public long? ParentId { get; set; }

    /// <summary>
    /// Gets or sets a globally unique identifier for the train execution.
    /// </summary>
    /// <remarks>
    /// The ExternalId is typically a GUID that can be used to reference the train
    /// from external systems. Unlike the database Id, this identifier is designed
    /// to be shared across system boundaries.
    /// </remarks>
    [Column("external_id")]
    public string ExternalId { get; set; } = null!;

    /// <summary>
    /// Gets or sets the name of the train.
    /// </summary>
    /// <remarks>
    /// The train's canonical name: the FullName of the interface it was registered under, or the
    /// concrete type's FullName for a train resolved outside dependency injection. Filter runs of a
    /// train by the interface FullName, not the class name.
    /// </remarks>
    [Column("name")]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Gets the name of the assembly that executed the train.
    /// </summary>
    /// <remarks>
    /// The Executor identifies the source of the train execution, which is useful
    /// in distributed systems where trains might be executed by different services.
    /// </remarks>
    [Column("executor")]
    public string? Executor { get; private set; }

    /// <summary>
    /// Gets or sets the current state of the train.
    /// </summary>
    /// <remarks>
    /// The TrainState tracks the lifecycle of the train execution,
    /// from Pending through InProgress to either Completed or Failed.
    /// This is a key property for monitoring and reporting on train status.
    /// </remarks>
    [Column("train_state")]
    public TrainState TrainState { get; set; }

    /// <summary>
    /// Gets the name of the junction where the train failed, if applicable.
    /// </summary>
    /// <remarks>
    /// When a train fails, this property identifies the specific junction
    /// that encountered the error, making it easier to diagnose issues.
    /// </remarks>
    [Column("failure_junction")]
    public string? FailureJunction { get; private set; }

    /// <summary>
    /// Gets the type of exception that caused the train to fail, if applicable.
    /// </summary>
    /// <remarks>
    /// This property stores the fully qualified name of the exception class
    /// that caused the train failure, enabling categorization of errors.
    /// </remarks>
    [Column("failure_exception")]
    public string? FailureException { get; private set; }

    /// <summary>
    /// Gets the error message associated with the train failure, if applicable.
    /// </summary>
    /// <remarks>
    /// This property contains the human-readable description of what went wrong,
    /// typically derived from the Exception.Message property.
    /// </remarks>
    [Column("failure_reason")]
    public string? FailureReason { get; private set; }

    /// <summary>
    /// What kind of failure this was, as classified where it happened.
    /// <see cref="FailureClass.Unclassified"/> when nothing classified it.
    /// </summary>
    [Column("failure_class")]
    public FailureClass FailureClass { get; internal set; } = FailureClass.Unclassified;

    /// <summary>
    /// Gets or sets the stack trace associated with the train failure, if applicable.
    /// </summary>
    /// <remarks>
    /// The stack trace provides detailed information about the sequence of method calls
    /// that led to the exception, which is valuable for debugging complex issues.
    /// </remarks>
    [Column("stack_trace")]
    public string? StackTrace { get; set; }

    /// <summary>
    /// Gets or sets the serialized input data for the train.
    /// </summary>
    /// <remarks>
    /// The Input property stores the serialized form of the data that was provided
    /// to the train when it was executed. This is useful for reproducing issues
    /// and understanding the context of the train execution.
    /// </remarks>
    [Column("input")]
    [JsonIgnore]
    public string? Input { get; set; }

    /// <summary>
    /// Gets or sets the serialized output data from the train.
    /// </summary>
    /// <remarks>
    /// The Output property stores the serialized form of the data that was produced
    /// by the train when it completed successfully. This allows for analysis of
    /// train results and verification of expected outcomes.
    /// </remarks>
    [Column("output")]
    public string? Output { get; set; }

    /// <summary>
    /// Gets or sets the time when the train execution started.
    /// </summary>
    /// <remarks>
    /// The StartTime is recorded when the train is initialized and begins execution.
    /// This is used for tracking execution duration and for time-based analysis.
    /// </remarks>
    [Column("start_time")]
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Gets or sets the time when the train execution completed or failed.
    /// </summary>
    /// <remarks>
    /// The EndTime is recorded when the train reaches a terminal state (Completed or Failed).
    /// This property, along with StartTime, allows for calculation of execution duration
    /// and identification of long-running trains.
    /// </remarks>
    [Column("end_time")]
    public DateTime? EndTime { get; set; }

    /// <summary>
    /// Gets or sets the time when this execution was scheduled to run.
    /// </summary>
    /// <remarks>
    /// This is the time the job was *supposed* to run, as determined by the scheduler,
    /// as opposed to <see cref="StartTime"/> which is when it actually started.
    ///
    /// Useful for:
    /// - SLA tracking (how long between scheduled time and actual start?)
    /// - Understanding execution delays due to queue backlog
    /// - Debugging scheduling issues
    ///
    /// This property is null for manually triggered jobs or jobs created before
    /// the scheduling system was implemented.
    /// </remarks>
    [Column("scheduled_time")]
    public DateTime? ScheduledTime { get; set; }

    /// <summary>
    /// The persisted request to cancel this run. Set to true by a cancel from the dashboard, the
    /// GraphQL operations, <c>ITraxScheduler</c>, or the scheduler's job-timeout sweep; defaults to
    /// false.
    /// </summary>
    /// <remarks>
    /// Setting it does not stop a run by itself. A run notices it only at its next junction
    /// boundary, and only when the cancellation check junction effect is registered; the run then
    /// ends <c>Cancelled</c> rather than <c>Failed</c>. The flag stays set on the finished row.
    /// </remarks>
    [Column("cancel_requested")]
    public bool CancellationRequested { get; set; }

    /// <summary>
    /// UTC time the junction named by <see cref="CurrentlyRunningJunction"/> started. Written by the
    /// junction progress effect before each junction and cleared after it and when the run
    /// finishes; always null when that effect is not registered.
    /// </summary>
    [Column("junction_started_at")]
    public DateTime? JunctionStartedAt { get; set; }

    /// <summary>
    /// Short class name of the junction executing right now, for live progress. Written by the
    /// junction progress effect before each junction and cleared to null after it and when the run
    /// finishes; always null when that effect is not registered.
    /// </summary>
    [Column("currently_running_junction")]
    public string? CurrentlyRunningJunction { get; set; }

    /// <summary>
    /// Gets or sets the hostname of the machine where the train executed.
    /// </summary>
    [Column("host_name")]
    public string? HostName { get; set; }

    /// <summary>
    /// Gets or sets the environment type where the train executed
    /// (e.g., <c>"lambda"</c>, <c>"ecs"</c>, <c>"kubernetes"</c>, <c>"azure-app-service"</c>, <c>"server"</c>).
    /// </summary>
    [Column("host_environment")]
    public string? HostEnvironment { get; set; }

    /// <summary>
    /// Gets or sets the instance-level identifier of the host
    /// (e.g., Lambda log stream, ECS task ID, Kubernetes pod name, or <c>{MachineName}-{PID}</c>).
    /// </summary>
    [Column("host_instance_id")]
    public string? HostInstanceId { get; set; }

    /// <summary>
    /// Gets or sets user-provided key-value labels serialized as JSON
    /// (e.g., region, service, team). Stored as JSONB in PostgreSQL.
    /// </summary>
    [Column("host_labels")]
    [JsonIgnore]
    public string? HostLabels { get; set; }

    /// <summary>
    /// Gets a value indicating whether this train is a child of another train.
    /// </summary>
    /// <remarks>
    /// This computed property provides a convenient way to check if the train
    /// was triggered by another train, based on whether ParentId has a value.
    /// </remarks>
    public bool IsChild => ParentId is not null;

    /// <summary>
    /// Private field to store the deserialized input object for the train.
    /// </summary>
    /// <remarks>
    /// This field holds the actual input object that was provided to the train.
    /// It is not persisted to the database directly, but is used during train execution
    /// and is serialized to the Input property for persistence.
    /// </remarks>
    private dynamic? _inputObject;

    /// <summary>
    /// Private field to store the deserialized output object from the train.
    /// </summary>
    /// <remarks>
    /// This field holds the actual output object that was produced by the train.
    /// It is not persisted to the database directly, but is used during train execution
    /// and is serialized to the Output property for persistence.
    /// </remarks>
    private dynamic? _outputObject;

    #endregion

    #region ForeignKeys

    /// <summary>
    /// Gets or sets the identifier of the manifest that defines this train execution.
    /// </summary>
    /// <remarks>
    /// This property establishes the relationship between a train execution (Metadata)
    /// and its job definition (Manifest). A single Manifest can have many Metadata records.
    /// </remarks>
    [Column("manifest_id")]
    [JsonPropertyName("manifest_id")]
    [JsonInclude]
    public long? ManifestId { get; set; }

    /// <summary>
    /// The run whose recorded decisions this run replays, or null for a run that asks its
    /// deciders afresh.
    /// </summary>
    /// <remarks>
    /// Set when a run is requeued: the new run takes the tracks the original took instead of
    /// asking again and possibly being answered differently. It follows the chain back, so a
    /// question the named run never reached takes the answer of the run that one replayed. Not a
    /// foreign key, because the original may be deleted first; a run whose chain names a run that
    /// no longer exists, belongs to another train, or ran without recording its decisions
    /// (<see cref="DecisionsRecorded"/>) fails before its first junction, classified permanent,
    /// rather than asking afresh, unless it is a manifest's retry, which asks afresh and is marked
    /// <see cref="ReplayAbandoned"/>. The chain stops at a run so marked. A question no run in the
    /// chain reached is asked afresh.
    /// </remarks>
    [Column("replay_decisions_of")]
    [JsonPropertyName("replay_decisions_of")]
    [JsonInclude]
    public long? ReplayDecisionsOf { get; set; }

    /// <summary>
    /// The run this run resumes, or null for a run that ran its chain from the top.
    /// </summary>
    /// <remarks>
    /// Carried from the work queue entry at dispatch. A resumed run writes only the checkpoints after its
    /// resume point, so the latest checkpoint is followed back through this link: a run's checkpoints are
    /// its own rows and those of the run it resumed. Not a foreign key, mirroring
    /// <see cref="ReplayDecisionsOf"/>: metadata cleanup keeps a run while a queued entry or a run that
    /// stays names it here, and deletes a resumed run with its source when both have expired.
    /// </remarks>
    [Column("resume_from")]
    [JsonPropertyName("resume_from")]
    [JsonInclude]
    public long? ResumeFrom { get; set; }

    /// <summary>
    /// The node id this run resumed at, or null for after the latest checkpoint of the run named by
    /// <see cref="ResumeFrom"/>. Carried from the work queue entry at dispatch.
    /// </summary>
    [Column("resume_at")]
    [JsonPropertyName("resume_at")]
    [JsonInclude]
    public string? ResumeAt { get; set; }

    /// <summary>
    /// The machine whose invoking state queued this run, carried from its work queue entry at dispatch; null for
    /// every other run. The scheduler never retries such a run and an operator cannot requeue it: its machine
    /// retries by entering the state again, which queues a new run.
    /// </summary>
    [Column("invoking_machine")]
    [JsonPropertyName("invoking_machine")]
    [JsonInclude]
    public string? InvokingMachine { get; set; }

    /// <summary>The instance whose invoking state queued this run; null unless <see cref="InvokingMachine"/> is set.</summary>
    [Column("invoking_instance_id")]
    [JsonPropertyName("invoking_instance_id")]
    [JsonInclude]
    public Guid? InvokingInstanceId { get; set; }

    /// <summary>Whether a user or the system owns the instance that queued this run; null unless <see cref="InvokingMachine"/> is set.</summary>
    [Column("invoking_owner_kind")]
    [JsonPropertyName("invoking_owner_kind")]
    [JsonInclude]
    public SnapshotOwnerKind? InvokingOwnerKind { get; set; }

    /// <summary>
    /// The output of a completed invoked run, serialized as its machine's <c>OnDone</c> edges read it
    /// (<see cref="Utils.InvokedRunOutput"/>), and written in the run's terminal write so the outcome survives
    /// any crash after it. Null on every other run, on a run that has not completed, on one whose output was
    /// <see cref="InvokeOutputOversize"/>, and on one whose output could not be serialized. Internal and never
    /// serialized: it is not part of any operator view, broadcast or lifecycle payload.
    /// </summary>
    internal string? InvokeOutput { get; set; }

    /// <summary>
    /// True when a completed invoked run's output was larger than <see cref="Utils.InvokedRunOutput.MaxBytes"/>,
    /// so it was not stored and the invoking state fails instead. Internal, like <see cref="InvokeOutput"/>.
    /// </summary>
    internal bool InvokeOutputOversize { get; set; }

    /// <summary>
    /// Records <paramref name="output"/> for the machine that invoked this run, when one did: the copy its
    /// <c>OnDone</c> edges read, or the mark that it was too large. An output that cannot be serialized is left
    /// unrecorded, which the machine treats as a failure, and the exception is returned for the log.
    /// </summary>
    internal Exception? RecordInvokeOutput(object? output, Type declaredType)
    {
        if (InvokingMachine is null)
            return null;

        try
        {
            (InvokeOutput, InvokeOutputOversize) = Utils.InvokedRunOutput.Serialize(
                output,
                declaredType
            );
            return null;
        }
        catch (Exception ex)
        {
            InvokeOutput = null;
            InvokeOutputOversize = false;
            return ex;
        }
    }

    /// <summary>
    /// True when the run started on a host that records decisions (<c>AddDecisionRecording</c>),
    /// so every decision it made is in <c>trax.decision</c>; false when what it decided, if
    /// anything, was never recorded.
    /// </summary>
    /// <remarks>
    /// Set by <c>ServiceTrain.Run</c> on the run's first write, before any junction, so a run
    /// killed mid-way still carries it. A replay reads it to tell a run that reached no questions
    /// from one whose answers it cannot know.
    /// </remarks>
    [Column("decisions_recorded")]
    [JsonPropertyName("decisions_recorded")]
    [JsonInclude]
    public bool DecisionsRecorded { get; set; }

    /// <summary>
    /// True when the run named a run to replay (<see cref="ReplayDecisionsOf"/>) and asked its
    /// questions afresh instead, because the replay could not be honoured. Only a manifest's retry
    /// does that; any other run fails instead.
    /// </summary>
    /// <remarks>
    /// <see cref="ReplayDecisionsOf"/> is kept, as the record of what the run was queued to do. A
    /// later replay of this run stops here rather than going on to the run it named, whose answers
    /// this run never acted on: it replays this run's own answers when this run recorded them, and
    /// fails, as for any run that did not record its decisions, when it did not.
    /// </remarks>
    [Column("replay_abandoned")]
    [JsonPropertyName("replay_abandoned")]
    [JsonInclude]
    public bool ReplayAbandoned { get; set; }

    /// <summary>
    /// Gets the manifest that defines this train execution.
    /// </summary>
    /// <remarks>
    /// This navigation property allows for accessing the job definition (Manifest)
    /// from a train execution record. It is populated by the ORM when the metadata is
    /// loaded from the database.
    /// </remarks>
    public Manifest.Manifest? Manifest { get; private set; }

    /// <summary>
    /// Gets the parent train metadata, if this train is a child train.
    /// </summary>
    /// <remarks>
    /// This navigation property allows for traversal of the train hierarchy
    /// from child to parent. It is populated by the ORM when the metadata is
    /// loaded from the database.
    /// </remarks>
    public Metadata Parent { get; private set; } = null!;

    /// <summary>
    /// Gets the collection of child train metadata records, if this train
    /// has triggered other trains.
    /// </summary>
    /// <remarks>
    /// This navigation property allows for traversal of the train hierarchy
    /// from parent to children. It is populated by the ORM when the metadata is
    /// loaded from the database.
    /// </remarks>
    public ICollection<Metadata> Children { get; private set; } = null!;

    /// <summary>
    /// Gets the collection of log entries associated with this train.
    /// </summary>
    /// <remarks>
    /// This navigation property provides access to the detailed log entries
    /// that were recorded during the train execution. It is populated by
    /// the ORM when the metadata is loaded from the database.
    /// </remarks>
    public ICollection<Log.Log> Logs { get; private set; } = null!;

    #endregion

    #region Functions

    /// <summary>
    /// Creates a new Metadata instance with the specified properties.
    /// </summary>
    /// <param name="metadata">The data transfer object containing the initial metadata values</param>
    /// <returns>A new Metadata instance</returns>
    /// <remarks>
    /// This factory method is the preferred way to create new Metadata instances.
    /// It initializes the metadata with default values and the specified properties,
    /// ensuring that all required fields are properly set.
    ///
    /// The method:
    /// 1. Sets the Name and Input from the provided DTO
    /// 2. Generates a new ExternalId as a GUID
    /// 3. Sets the initial TrainState to Pending
    /// 4. Determines the Executor from the entry assembly
    /// 5. Sets the StartTime to the current UTC time
    /// 6. Sets the ParentId if provided
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static Metadata Create(CreateMetadata metadata)
    {
        var host = TraxHostInfo.Current;

        var newTrain = new Metadata
        {
            Name = metadata.Name,
            ExternalId = metadata.ExternalId,
            TrainState = TrainState.Pending,
            Executor = Assembly.GetEntryAssembly()?.GetAssemblyProject(),
            StartTime = DateTime.UtcNow,
            ParentId = metadata.ParentId,
            ManifestId = metadata.ManifestId,
            ReplayDecisionsOf = metadata.ReplayDecisionsOf,
            ResumeFrom = metadata.ResumeFrom,
            ResumeAt = metadata.ResumeAt,
            InvokingMachine = metadata.InvokedBy?.Machine,
            InvokingInstanceId = metadata.InvokedBy?.InstanceId,
            InvokingOwnerKind = metadata.InvokedBy?.OwnerKind,
            HostName = host?.HostName,
            HostEnvironment = host?.HostEnvironment,
            HostInstanceId = host?.HostInstanceId,
            HostLabels = host?.Labels is { Count: > 0 }
                ? JsonSerializer.Serialize(host.Labels)
                : null,
        };

        newTrain.SetInputObject(metadata.Input);

        return newTrain;
    }

    /// <summary>
    /// Adds exception details to this metadata record.
    /// </summary>
    /// <param name="trainException">The exception that caused the train to fail</param>
    /// <returns>A Unit value (similar to void, but functional)</returns>
    /// <remarks>
    /// This method extracts information from the provided exception and populates
    /// the failure-related properties of the metadata record. It attempts to deserialize
    /// the exception message as a TrainExceptionData object, which provides structured
    /// information about the failure. If deserialization fails, it falls back to extracting
    /// information directly from the exception.
    ///
    /// The method sets:
    /// 1. FailureException - The type of the exception
    /// 2. FailureReason - The error message
    /// 3. FailureJunction - The junction where the failure occurred
    /// 4. StackTrace - The stack trace of the exception
    ///
    /// This information is valuable for diagnosing and analyzing train failures.
    ///
    /// <para>The failure class is read from attached data, or from the message of a
    /// <see cref="TrainException"/>, which is the type a recorded failure is rebuilt as. Any other
    /// exception's message is its own text, so a class in it is not recorded. A value outside
    /// <see cref="Trax.Core.Exceptions.FailureClass"/> is recorded as
    /// <see cref="Trax.Core.Exceptions.FailureClass.Unclassified"/>, as the remote wire already
    /// does.</para>
    /// </remarks>
    public Unit AddException(Exception trainException)
    {
        // Priority 1: Structured data attached to the exception object (local execution)
        if (trainException.Data["TrainExceptionData"] is TrainExceptionData data)
        {
            FailureException = data.Type;
            FailureReason = data.Message;
            FailureJunction = data.Junction;
            StackTrace = data.StackTrace ?? trainException.StackTrace;
            if (data.FailureClass is { } local)
                FailureClass = Defined(local);
            return Unit.Default;
        }

        // Priority 2: JSON-serialized TrainExceptionData in the message (remote execution / legacy).
        // The class is taken only from a TrainException, the type a recorded failure is rebuilt as.
        try
        {
            var deserialized = JsonSerializer.Deserialize<TrainExceptionData>(
                trainException.Message
            );
            if (deserialized != null)
            {
                FailureException = deserialized.Type;
                FailureReason = deserialized.Message;
                if (trainException is TrainException && deserialized.FailureClass is { } remote)
                    FailureClass = Defined(remote);
                FailureJunction = deserialized.Junction;
                StackTrace = deserialized.StackTrace ?? trainException.StackTrace;
                return Unit.Default;
            }
        }
        catch { }

        // Priority 3: Plain exception (no Trax context)
        FailureException = trainException.GetType().Name;
        FailureReason = trainException.Message;
        FailureJunction = "TrainException";
        StackTrace = trainException.StackTrace;
        return Unit.Default;
    }

    /// <summary>
    /// The stored output of a run whose output could not be written.
    /// </summary>
    internal const string UnrecordedOutput = """{"_unrecorded": true}""";

    /// <summary>
    /// The stored failure message of a run whose failure detail could not be written.
    /// </summary>
    internal const string UnrecordedFailureReason =
        "The failure's message could not be recorded; see the host's log for it.";

    /// <summary>
    /// Replaces what a terminal write carries beyond the run's state (its output, and the failure's
    /// message and stack trace) with fixed text any store accepts, so the state and end time can
    /// still be written when the store refused the row for its content. The failure's type,
    /// junction and class are kept: Trax produces those, not the train.
    /// </summary>
    internal void DropOutcomeContent()
    {
        if (Output is not null)
            Output = UnrecordedOutput;

        if (FailureReason is not null)
            FailureReason = UnrecordedFailureReason;

        StackTrace = null;
    }

    /// <summary>
    /// Puts back the outcome content <see cref="DropOutcomeContent"/> replaced, for the lifecycle
    /// hooks: they report what the run did, not what the store could hold.
    /// </summary>
    internal void RestoreOutcomeContent(string? output, string? failureReason, string? stackTrace)
    {
        Output = output;
        FailureReason = failureReason;
        StackTrace = stackTrace;
    }

    /// <summary>
    /// Replaces a stored input the store refused with the same placeholder an unrecorded output
    /// gets, so the run's row can still be written. The input object the train runs with is kept.
    /// </summary>
    internal void DropInputContent()
    {
        if (Input is not null)
            Input = UnrecordedOutput;
    }

    /// <summary>
    /// Maps a failure class outside the enum to <see cref="Trax.Core.Exceptions.FailureClass.Unclassified"/>,
    /// matching <c>RemoteRunJson.TolerantFailureClassConverter</c>. An undefined value cannot be
    /// stored by the Postgres enum column, and on SQLite it breaks every later read of the row.
    /// </summary>
    private static FailureClass Defined(FailureClass failureClass) =>
        Enum.IsDefined(failureClass) ? failureClass : FailureClass.Unclassified;

    /// <summary>
    /// Drops the in-memory input and output objects and nulls the <see cref="Input"/> and
    /// <see cref="Output"/> JSON on this instance so they can be collected. The service train calls
    /// it from its own <c>Dispose</c>. It does not touch the database row, but saving a tracked
    /// instance after disposing it would write those columns as null.
    /// </summary>
    public void Dispose()
    {
        _inputObject = null;
        _outputObject = null;
        Input = null;
        Output = null;
    }

    /// <summary>
    /// Serializes this run to JSON for a log line, leaving out the <c>Manifest</c>, <c>Parent</c>, <c>Children</c> and <c>Logs</c> navigations. Not a stable format: read the
    /// properties for values.
    /// </summary>
    public override string ToString() =>
        JsonSerializer.Serialize(
            this,
            GetType(),
            TraxLogSerialization.ForLogging(
                TraxEffectConfiguration.StaticSystemJsonSerializerOptions
            )
        );

    /// <summary>
    /// Sets the deserialized input object for the train.
    /// </summary>
    /// <param name="value">The input object to set</param>
    /// <remarks>
    /// This method provides controlled access to set the input object.
    /// Using methods instead of properties prevents the object from being
    /// included in logging serialization by frameworks like Serilog.
    /// </remarks>
    public void SetInputObject(dynamic? value) => _inputObject = value;

    /// <summary>
    /// Gets the deserialized input object for the train.
    /// </summary>
    /// <returns>The input object, or null if not set</returns>
    /// <remarks>
    /// This method provides controlled access to retrieve the input object.
    /// Using methods instead of properties prevents the object from being
    /// included in logging serialization by frameworks like Serilog.
    /// </remarks>
    public dynamic? GetInputObject() => _inputObject;

    /// <summary>
    /// Sets the deserialized output object from the train.
    /// </summary>
    /// <param name="value">The output object to set</param>
    /// <remarks>
    /// This method provides controlled access to set the output object.
    /// Using methods instead of properties prevents the object from being
    /// included in logging serialization by frameworks like Serilog.
    /// </remarks>
    public void SetOutputObject(dynamic? value) => _outputObject = value;

    /// <summary>
    /// Gets the deserialized output object from the train.
    /// </summary>
    /// <returns>The output object, or null if not set</returns>
    /// <remarks>
    /// This method provides controlled access to retrieve the output object.
    /// Using methods instead of properties prevents the object from being
    /// included in logging serialization by frameworks like Serilog.
    /// </remarks>
    public dynamic? GetOutputObject() => _outputObject;

    /// <summary>
    /// Gets the deserialized input object cast to the specified type.
    /// The input is set before the train's junctions execute, so it is available in all
    /// lifecycle hooks (<c>OnStarted</c>, <c>OnCompleted</c>, <c>OnFailed</c>, <c>OnCancelled</c>).
    /// Returns <c>default</c> if the input is null or not of type <typeparamref name="T"/>.
    /// </summary>
    /// <remarks>
    /// For per-train lifecycle hooks, prefer the <c>TrainInput</c> property on
    /// <c>ServiceTrain&lt;TIn, TOut&gt;</c> which provides the same value without
    /// requiring a type parameter.
    /// </remarks>
    public T? GetInput<T>() => _inputObject is T typed ? typed : default;

    /// <summary>
    /// Gets the deserialized output object cast to the specified type.
    /// The output is set after a successful run, so it is only meaningful in <c>OnCompleted</c>.
    /// Returns <c>default</c> in <c>OnStarted</c>, <c>OnFailed</c>, and <c>OnCancelled</c>.
    /// Returns <c>default</c> if the output is null or not of type <typeparamref name="T"/>.
    /// </summary>
    /// <remarks>
    /// For per-train lifecycle hooks, prefer the <c>TrainOutput</c> property on
    /// <c>ServiceTrain&lt;TIn, TOut&gt;</c> which provides the same value without
    /// requiring a type parameter.
    /// </remarks>
    public T? GetOutput<T>() => _outputObject is T typed ? typed : default;

    #endregion

    /// <summary>
    /// Initializes a new instance of the Metadata class.
    /// </summary>
    /// <remarks>
    /// This constructor is used by the JSON serializer when deserializing
    /// metadata from JSON. It is marked with the JsonConstructor attribute
    /// to indicate that it should be used for deserialization.
    ///
    /// The constructor is parameterless because the serializer will set
    /// the properties after construction using property setters.
    /// </remarks>
    [JsonConstructor]
    public Metadata() { }
}
