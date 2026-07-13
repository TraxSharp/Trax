using System;
using System.Collections.Generic;

namespace Trax.Effect.Provider.Parameter.Configuration;

/// <summary>
/// Runtime configuration for the Parameter Effect provider.
/// Controls which train parameters (input and/or output) are serialized to the metadata record.
/// </summary>
/// <remarks>
/// This configuration is registered as a singleton and can be modified at runtime via the dashboard.
/// Changes take effect on the next train execution scope.
/// </remarks>
public class ParameterEffectConfiguration
{
    /// <summary>
    /// Whether to serialize train input parameters to <c>Metadata.Input</c>.
    /// </summary>
    public bool SaveInputs { get; set; } = true;

    /// <summary>
    /// Whether to serialize train output parameters to <c>Metadata.Output</c>.
    /// </summary>
    public bool SaveOutputs { get; set; } = true;

    /// <summary>
    /// Hard byte ceiling per serialized parameter (applies to input <b>and</b> output).
    /// <c>null</c> means unbounded (the historical behavior).
    /// </summary>
    /// <remarks>
    /// A payload that serializes past this many UTF-8 bytes is aborted mid-serialization
    /// (before the whole thing is materialized) and stored as a small valid-JSON placeholder
    /// <c>{"_truncated": true, "_maxBytes": N}</c>. This is the automatic safety net that keeps
    /// a single unexpectedly-large train from exhausting host memory. Must be a positive value.
    /// </remarks>
    public int? MaxParameterBytes { get; set; }

    /// <summary>
    /// Optional predicate deciding whether a given train's OUTPUT should be serialized.
    /// Receives the canonical train name (<c>Metadata.Name</c>) and returns <c>false</c> to skip.
    /// <c>null</c> means no predicate. Evaluated in addition to <see cref="ExcludeOutput(string)"/>.
    /// </summary>
    /// <remarks>
    /// This is the escape hatch for cases the type/string helpers can't express. For the common
    /// case (a known list of large fetch trains) prefer <see cref="ExcludeOutput{TTrain}"/>.
    /// </remarks>
    public Func<string, bool>? ShouldSaveOutputs { get; set; }

    /// <summary>
    /// Name fragments whose trains skip OUTPUT serialization. Matched with
    /// <c>Metadata.Name.Contains(fragment)</c>, so a type's <c>FullName</c> matches whether the
    /// canonical name is that name exactly or embeds it (e.g. an assembly-qualified request type).
    /// </summary>
    internal HashSet<string> OutputExclusions { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Skips OUTPUT serialization for trains whose canonical name contains <paramref name="trainNameFragment"/>.
    /// </summary>
    public ParameterEffectConfiguration ExcludeOutput(string trainNameFragment)
    {
        ArgumentException.ThrowIfNullOrEmpty(trainNameFragment);
        OutputExclusions.Add(trainNameFragment);
        return this;
    }

    /// <summary>
    /// Skips OUTPUT serialization for trains whose canonical name contains <paramref name="type"/>'s
    /// <c>FullName</c>. Pass the type that appears in the train's canonical name (the train interface for
    /// named routes, or the request/query type for trains dispatched by input type).
    /// </summary>
    public ParameterEffectConfiguration ExcludeOutput(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        OutputExclusions.Add(
            type.FullName
                ?? throw new ArgumentException(
                    $"Type '{type}' has no FullName and cannot be used as an output exclusion.",
                    nameof(type)
                )
        );
        return this;
    }

    /// <summary>
    /// Skips OUTPUT serialization for trains whose canonical name contains <typeparamref name="TTrain"/>'s
    /// <c>FullName</c>. See <see cref="ExcludeOutput(Type)"/> for which type to pass.
    /// </summary>
    public ParameterEffectConfiguration ExcludeOutput<TTrain>() => ExcludeOutput(typeof(TTrain));

    /// <summary>
    /// Whether the output of the train with the given canonical name should be serialized,
    /// considering both the exclusion set and the optional predicate.
    /// </summary>
    internal bool ShouldSaveOutputFor(string? name)
    {
        if (name is not null && OutputExclusions.Count > 0)
            foreach (var fragment in OutputExclusions)
                if (name.Contains(fragment, StringComparison.Ordinal))
                    return false;

        return ShouldSaveOutputs?.Invoke(name ?? string.Empty) ?? true;
    }
}
