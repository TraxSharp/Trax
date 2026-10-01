namespace Trax.Api.DTOs;

/// <summary>
/// A registered observational effect and its runtime state, as seen by THIS process. The effect
/// registry is an in-memory per-process singleton with no persistence or cross-process broadcast,
/// so <c>operations.setEffectEnabled</c> changes this process only, not the scheduler or worker
/// processes where trains usually run, and a restart restores the configured state. Backs the
/// dashboard's effects list.
/// </summary>
/// <param name="Name">The effect provider factory's type name.</param>
/// <param name="FullName">The effect provider factory's full type name, which identifies it.</param>
/// <param name="Enabled">Whether the effect currently runs in this process.</param>
/// <param name="Toggleable">Whether the effect registry allows the effect to be enabled and disabled at runtime.</param>
/// <param name="IsConfigurable">
/// Whether the effect's factory exposes runtime settings (it implements
/// <c>IConfigurableProviderFactory</c>).
/// </param>
/// <param name="ConfigurationTypeName">The full name of the settings type, when configurable.</param>
/// <param name="Configuration">
/// The factory's current settings serialized as JSON, when configurable. Settings can hold
/// credentials, so this is only reachable through the <c>operations</c> namespace and its gate,
/// the same gate that guards an execution's input.
/// </param>
public record EffectInfo(
    string Name,
    string FullName,
    bool Enabled,
    bool Toggleable,
    bool IsConfigurable = false,
    string? ConfigurationTypeName = null,
    string? Configuration = null
);
