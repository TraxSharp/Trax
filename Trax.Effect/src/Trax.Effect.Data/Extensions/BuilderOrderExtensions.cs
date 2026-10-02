using System.ComponentModel;
using Microsoft.Extensions.Logging;
using Trax.Effect.Configuration.TraxEffectBuilder;

namespace Trax.Effect.Data.Extensions;

/// <summary>
/// Overloads that exist only to turn a builder call made in the wrong order into a compile error
/// that says which call comes first.
/// </summary>
/// <remarks>
/// <c>AddDataContextLogging</c> is defined on <see cref="TraxEffectBuilderWithData"/>, the stage a
/// data provider promotes the builder to. Called before one, the compiler would report CS1929,
/// naming the builder state types and leaving the reader to work out the order from them. The
/// overload here takes the earlier stage, is marked obsolete as an error carrying the instruction,
/// and is hidden from completion. It cannot run: a call that binds to it does not compile. The
/// correct order binds to the real method, whose receiver type is more specific.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class BuilderOrderExtensions
{
    internal const string DataProviderFirst =
        "Call UsePostgres(...), UseSqlite(...) or UseInMemory(...) before AddDataContextLogging(...).";

    /// <summary>Not callable: <c>AddDataContextLogging</c> comes after a data provider.</summary>
    [Obsolete(DataProviderFirst, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TraxEffectBuilderWithData AddDataContextLogging(
        this TraxEffectBuilder configurationBuilder,
        LogLevel? minimumLogLevel = null,
        List<string>? blacklist = null
    ) => throw new InvalidOperationException(DataProviderFirst);

    internal const string DataProviderBeforeDecisions =
        "Call UsePostgres(...), UseSqlite(...) or UseInMemory(...) before AddDecisionRecording().";

    /// <summary>Not callable: <c>AddDecisionRecording</c> comes after a data provider.</summary>
    [Obsolete(DataProviderBeforeDecisions, error: true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static TraxEffectBuilderWithData AddDecisionRecording(
        this TraxEffectBuilder configurationBuilder
    ) => throw new InvalidOperationException(DataProviderBeforeDecisions);
}
