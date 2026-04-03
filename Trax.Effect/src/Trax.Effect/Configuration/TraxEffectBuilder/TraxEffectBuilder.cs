using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Utils;

namespace Trax.Effect.Configuration.TraxEffectBuilder;

/// <summary>
/// Builder for configuring the Trax effect system (data providers, junction providers, lifecycle hooks).
/// </summary>
/// <remarks>
/// After calling a data provider method (<c>UsePostgres()</c>, <c>UseSqlite()</c>, or <c>UseInMemory()</c>), the builder
/// is promoted to <see cref="TraxEffectBuilderWithData"/>, which unlocks additional methods such as
/// <c>AddDataContextLogging()</c>. All general effect methods (e.g., <c>AddJson()</c>,
/// <c>SaveTrainParameters()</c>) are available on both types via generic self-type preservation.
/// </remarks>
public partial class TraxEffectBuilder
{
    private readonly TraxBuilder.TraxBuilder _parent;

    internal TraxEffectBuilder(TraxBuilder.TraxBuilder parent)
    {
        _parent = parent;
    }

    /// <summary>
    /// Internal constructor for subtype promotion (e.g., <see cref="TraxEffectBuilderWithData"/>).
    /// Shares the same parent — all mutable state is on the parent or on <see cref="ServiceCollection"/>.
    /// </summary>
    internal TraxEffectBuilder(TraxEffectBuilder source)
        : this(source._parent)
    {
        // Copy builder-level state so the promoted instance carries forward
        // any configuration set before promotion.
        MigrationsDisabled = source.MigrationsDisabled;
        JunctionProgressEnabled = source.JunctionProgressEnabled;
        DataContextLoggingEffectEnabled = source.DataContextLoggingEffectEnabled;
        SerializeJunctionData = source.SerializeJunctionData;
        LogLevel = source.LogLevel;
        TrainParameterJsonSerializerOptions = source.TrainParameterJsonSerializerOptions;
    }

    /// <summary>
    /// Gets the service collection for registering services.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public IServiceCollection ServiceCollection => _parent.ServiceCollection;

    /// <summary>
    /// Gets the effect registry for toggling effect providers at runtime.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public IEffectRegistry? EffectRegistry => _parent.EffectRegistry;

    /// <summary>
    /// Whether a database-backed data provider (e.g., Postgres) was configured.
    /// Propagated to the root builder so downstream subsystems (e.g., the scheduler)
    /// can default to in-memory implementations when no database is available.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool HasDatabaseProvider
    {
        get => _parent.HasDatabaseProvider;
        set => _parent.HasDatabaseProvider = value;
    }

    /// <summary>
    /// Whether any data provider (<c>UsePostgres()</c>, <c>UseSqlite()</c>, or <c>UseInMemory()</c>) was configured.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool HasDataProvider
    {
        get => _parent.HasDataProvider;
        set => _parent.HasDataProvider = value;
    }

    /// <summary>
    /// When <c>true</c>, <see cref="Extensions.ServiceExtensions"/> skips the automatic database
    /// migration that normally runs inside <c>UsePostgres()</c>. Use this in Lambda runners or
    /// other environments where migrations are managed externally.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool MigrationsDisabled { get; set; }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool JunctionProgressEnabled { get; set; }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool DataContextLoggingEffectEnabled { get; set; } = false;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool SerializeJunctionData { get; set; } = false;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public LogLevel LogLevel { get; set; } = LogLevel.Debug;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public JsonSerializerOptions TrainParameterJsonSerializerOptions { get; set; } =
        TraxJsonSerializationOptions.Default;
}
