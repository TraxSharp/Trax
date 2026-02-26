using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Trax.Effect.Configuration.Trax.CoreEffectConfiguration;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Core.Exceptions;
using LanguageExt;

namespace Trax.Effect.Models.Metadata;

/// <summary>
/// Represents the metadata for a workflow execution in the Trax.Effect system.
/// This class implements the IMetadata interface and provides the concrete implementation
/// for tracking workflow execution details.
/// </summary>
/// <remarks>
/// The Metadata class is the central entity for workflow tracking in the system.
/// It stores comprehensive information about workflow executions, including:
///
/// 1. Identification and relationships (Id, ParentId, ExternalId)
/// 2. Basic workflow information (Name, Executor)
/// 3. State and timing (WorkflowState, StartTime, EndTime)
/// 4. Input and output data (Input, Output, InputObject, OutputObject)
/// 5. Error information (FailureStep, FailureException, FailureReason, StackTrace)
/// 6. Relationships to other entities (Parent, Children, Logs)
///
/// This class is designed to be persisted to a database and serves as the
/// primary record of workflow execution in the system.
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
    /// Gets or sets the identifier of the parent workflow, if this workflow
    /// was triggered by another workflow.
    /// </summary>
    /// <remarks>
    /// This property establishes parent-child relationships between workflows,
    /// enabling hierarchical tracking of complex workflow compositions.
    /// </remarks>
    [Column("parent_id")]
    [JsonPropertyName("parent_id")]
    [JsonInclude]
    public long? ParentId { get; set; }

    /// <summary>
    /// Gets or sets a globally unique identifier for the workflow execution.
    /// </summary>
    /// <remarks>
    /// The ExternalId is typically a GUID that can be used to reference the workflow
    /// from external systems. Unlike the database Id, this identifier is designed
    /// to be shared across system boundaries.
    /// </remarks>
    [Column("external_id")]
    public string ExternalId { get; set; }

    /// <summary>
    /// Gets or sets the name of the workflow.
    /// </summary>
    /// <remarks>
    /// The Name typically corresponds to the class name of the workflow implementation.
    /// This provides a human-readable identifier for the workflow type.
    /// </remarks>
    [Column("name")]
    public string Name { get; set; }

    /// <summary>
    /// Gets the name of the assembly that executed the workflow.
    /// </summary>
    /// <remarks>
    /// The Executor identifies the source of the workflow execution, which is useful
    /// in distributed systems where workflows might be executed by different services.
    /// </remarks>
    [Column("executor")]
    public string? Executor { get; private set; }

    /// <summary>
    /// Gets or sets the current state of the workflow.
    /// </summary>
    /// <remarks>
    /// The WorkflowState tracks the lifecycle of the workflow execution,
    /// from Pending through InProgress to either Completed or Failed.
    /// This is a key property for monitoring and reporting on workflow status.
    /// </remarks>
    [Column("workflow_state")]
    public WorkflowState WorkflowState { get; set; }

    /// <summary>
    /// Gets the name of the step where the workflow failed, if applicable.
    /// </summary>
    /// <remarks>
    /// When a workflow fails, this property identifies the specific step
    /// that encountered the error, making it easier to diagnose issues.
    /// </remarks>
    [Column("failure_step")]
    public string? FailureStep { get; private set; }

    /// <summary>
    /// Gets the type of exception that caused the workflow to fail, if applicable.
    /// </summary>
    /// <remarks>
    /// This property stores the fully qualified name of the exception class
    /// that caused the workflow failure, enabling categorization of errors.
    /// </remarks>
    [Column("failure_exception")]
    public string? FailureException { get; private set; }

    /// <summary>
    /// Gets the error message associated with the workflow failure, if applicable.
    /// </summary>
    /// <remarks>
    /// This property contains the human-readable description of what went wrong,
    /// typically derived from the Exception.Message property.
    /// </remarks>
    [Column("failure_reason")]
    public string? FailureReason { get; private set; }

    /// <summary>
    /// Gets or sets the stack trace associated with the workflow failure, if applicable.
    /// </summary>
    /// <remarks>
    /// The stack trace provides detailed information about the sequence of method calls
    /// that led to the exception, which is valuable for debugging complex issues.
    /// </remarks>
    [Column("stack_trace")]
    public string? StackTrace { get; set; }

    /// <summary>
    /// Gets or sets the serialized input data for the workflow.
    /// </summary>
    /// <remarks>
    /// The Input property stores the serialized form of the data that was provided
    /// to the workflow when it was executed. This is useful for reproducing issues
    /// and understanding the context of the workflow execution.
    /// </remarks>
    [Column("input")]
    [JsonIgnore]
    public string? Input { get; set; }

    /// <summary>
    /// Gets or sets the serialized output data from the workflow.
    /// </summary>
    /// <remarks>
    /// The Output property stores the serialized form of the data that was produced
    /// by the workflow when it completed successfully. This allows for analysis of
    /// workflow results and verification of expected outcomes.
    /// </remarks>
    [Column("output")]
    public string? Output { get; set; }

    /// <summary>
    /// Gets or sets the time when the workflow execution started.
    /// </summary>
    /// <remarks>
    /// The StartTime is recorded when the workflow is initialized and begins execution.
    /// This is used for tracking execution duration and for time-based analysis.
    /// </remarks>
    [Column("start_time")]
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Gets or sets the time when the workflow execution completed or failed.
    /// </summary>
    /// <remarks>
    /// The EndTime is recorded when the workflow reaches a terminal state (Completed or Failed).
    /// This property, along with StartTime, allows for calculation of execution duration
    /// and identification of long-running workflows.
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

    [Column("cancel_requested")]
    public bool CancellationRequested { get; set; }

    [Column("step_started_at")]
    public DateTime? StepStartedAt { get; set; }

    [Column("currently_running_step")]
    public string? CurrentlyRunningStep { get; set; }

    /// <summary>
    /// Gets a value indicating whether this workflow is a child of another workflow.
    /// </summary>
    /// <remarks>
    /// This computed property provides a convenient way to check if the workflow
    /// was triggered by another workflow, based on whether ParentId has a value.
    /// </remarks>
    public bool IsChild => ParentId is not null;

    /// <summary>
    /// Private field to store the deserialized input object for the workflow.
    /// </summary>
    /// <remarks>
    /// This field holds the actual input object that was provided to the workflow.
    /// It is not persisted to the database directly, but is used during workflow execution
    /// and is serialized to the Input property for persistence.
    /// </remarks>
    private dynamic? _inputObject;

    /// <summary>
    /// Private field to store the deserialized output object from the workflow.
    /// </summary>
    /// <remarks>
    /// This field holds the actual output object that was produced by the workflow.
    /// It is not persisted to the database directly, but is used during workflow execution
    /// and is serialized to the Output property for persistence.
    /// </remarks>
    private dynamic? _outputObject;

    #endregion

    #region ForeignKeys

    /// <summary>
    /// Gets or sets the identifier of the manifest that defines this workflow execution.
    /// </summary>
    /// <remarks>
    /// This property establishes the relationship between a workflow execution (Metadata)
    /// and its job definition (Manifest). A single Manifest can have many Metadata records.
    /// </remarks>
    [Column("manifest_id")]
    [JsonPropertyName("manifest_id")]
    [JsonInclude]
    public long? ManifestId { get; set; }

    /// <summary>
    /// Gets the manifest that defines this workflow execution.
    /// </summary>
    /// <remarks>
    /// This navigation property allows for accessing the job definition (Manifest)
    /// from a workflow execution record. It is populated by the ORM when the metadata is
    /// loaded from the database.
    /// </remarks>
    public Manifest.Manifest? Manifest { get; private set; }

    /// <summary>
    /// Gets the parent workflow metadata, if this workflow is a child workflow.
    /// </summary>
    /// <remarks>
    /// This navigation property allows for traversal of the workflow hierarchy
    /// from child to parent. It is populated by the ORM when the metadata is
    /// loaded from the database.
    /// </remarks>
    public Metadata Parent { get; private set; }

    /// <summary>
    /// Gets the collection of child workflow metadata records, if this workflow
    /// has triggered other workflows.
    /// </summary>
    /// <remarks>
    /// This navigation property allows for traversal of the workflow hierarchy
    /// from parent to children. It is populated by the ORM when the metadata is
    /// loaded from the database.
    /// </remarks>
    public ICollection<Metadata> Children { get; private set; }

    /// <summary>
    /// Gets the collection of log entries associated with this workflow.
    /// </summary>
    /// <remarks>
    /// This navigation property provides access to the detailed log entries
    /// that were recorded during the workflow execution. It is populated by
    /// the ORM when the metadata is loaded from the database.
    /// </remarks>
    public ICollection<Log.Log> Logs { get; private set; }

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
    /// 3. Sets the initial WorkflowState to Pending
    /// 4. Determines the Executor from the entry assembly
    /// 5. Sets the StartTime to the current UTC time
    /// 6. Sets the ParentId if provided
    /// </remarks>
    public static Metadata Create(CreateMetadata metadata)
    {
        var newWorkflow = new Metadata
        {
            Name = metadata.Name,
            ExternalId = Guid.NewGuid().ToString("N"),
            WorkflowState = WorkflowState.Pending,
            Executor = Assembly.GetEntryAssembly()?.GetAssemblyProject(),
            StartTime = DateTime.UtcNow,
            ParentId = metadata.ParentId,
            ManifestId = metadata.ManifestId
        };

        newWorkflow.SetInputObject(metadata.Input);

        return newWorkflow;
    }

    /// <summary>
    /// Adds exception details to this metadata record.
    /// </summary>
    /// <param name="workflowException">The exception that caused the workflow to fail</param>
    /// <returns>A Unit value (similar to void, but functional)</returns>
    /// <remarks>
    /// This method extracts information from the provided exception and populates
    /// the failure-related properties of the metadata record. It attempts to deserialize
    /// the exception message as a WorkflowExceptionData object, which provides structured
    /// information about the failure. If deserialization fails, it falls back to extracting
    /// information directly from the exception.
    ///
    /// The method sets:
    /// 1. FailureException - The type of the exception
    /// 2. FailureReason - The error message
    /// 3. FailureStep - The step where the failure occurred
    /// 4. StackTrace - The stack trace of the exception
    ///
    /// This information is valuable for diagnosing and analyzing workflow failures.
    /// </remarks>
    public Unit AddException(Exception workflowException)
    {
        try
        {
            var deserializedException = JsonSerializer.Deserialize<WorkflowExceptionData>(
                workflowException.Message
            );

            if (deserializedException == null)
            {
                FailureException = workflowException.GetType().Name;
                FailureReason = workflowException.Message;
                FailureStep = "WorkflowException";
                StackTrace = workflowException.StackTrace;
            }
            else
            {
                FailureException = deserializedException.Type;
                FailureReason = deserializedException.Message;
                FailureStep = deserializedException.Step;
                StackTrace = workflowException.StackTrace;
            }

            return Unit.Default;
        }
        catch (Exception)
        {
            FailureException = workflowException.GetType().Name;
            FailureReason = workflowException.Message;
            FailureStep = "WorkflowException";
            StackTrace = workflowException.StackTrace;
        }

        return Unit.Default;
    }

    public void Dispose()
    {
        _inputObject = null;
        _outputObject = null;
        Input = null;
        Output = null;
    }

    public override string ToString() =>
        JsonSerializer.Serialize(
            this,
            GetType(),
            Trax.CoreEffectConfiguration.StaticSystemJsonSerializerOptions
        );

    /// <summary>
    /// Sets the deserialized input object for the workflow.
    /// </summary>
    /// <param name="value">The input object to set</param>
    /// <remarks>
    /// This method provides controlled access to set the input object.
    /// Using methods instead of properties prevents the object from being
    /// included in logging serialization by frameworks like Serilog.
    /// </remarks>
    public void SetInputObject(dynamic? value) => _inputObject = value;

    /// <summary>
    /// Gets the deserialized input object for the workflow.
    /// </summary>
    /// <returns>The input object, or null if not set</returns>
    /// <remarks>
    /// This method provides controlled access to retrieve the input object.
    /// Using methods instead of properties prevents the object from being
    /// included in logging serialization by frameworks like Serilog.
    /// </remarks>
    public dynamic? GetInputObject() => _inputObject;

    /// <summary>
    /// Sets the deserialized output object from the workflow.
    /// </summary>
    /// <param name="value">The output object to set</param>
    /// <remarks>
    /// This method provides controlled access to set the output object.
    /// Using methods instead of properties prevents the object from being
    /// included in logging serialization by frameworks like Serilog.
    /// </remarks>
    public void SetOutputObject(dynamic? value) => _outputObject = value;

    /// <summary>
    /// Gets the deserialized output object from the workflow.
    /// </summary>
    /// <returns>The output object, or null if not set</returns>
    /// <remarks>
    /// This method provides controlled access to retrieve the output object.
    /// Using methods instead of properties prevents the object from being
    /// included in logging serialization by frameworks like Serilog.
    /// </remarks>
    public dynamic? GetOutputObject() => _outputObject;

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
