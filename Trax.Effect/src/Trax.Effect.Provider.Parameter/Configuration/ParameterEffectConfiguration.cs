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
}
