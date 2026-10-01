using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Radzen;
using Trax.Dashboard.Components.Dialogs;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Services.EffectRegistry;

namespace Trax.Dashboard.Components.Pages.Settings;

/// <summary>
/// The effects settings page, at <c>/trax/settings/effects</c>: lists every registered effect
/// provider, lets the user enable or disable the toggleable ones, and opens
/// <see cref="Dialogs.ConfigureEffectDialog"/> for configurable ones. Changes apply to this
/// process in memory and are not persisted. Shows a notice instead when no effect registry is
/// registered. Part of the dashboard UI, routed by the package; not intended to be used directly.
/// </summary>
public partial class EffectsSettingsPage
{
    [Inject]
    private IServiceProvider ServiceProvider { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [Inject]
    private DialogService DialogService { get; set; } = default!;

    // ── Effects state ──
    private IEffectRegistry? _effectRegistry;
    private bool _effectsAvailable;
    private List<EffectEntry> _effects = [];
    private Dictionary<Type, bool> _savedEffectStates = new();

    // ── Dirty tracking ──
    private bool IsEffectsDirty =>
        _effectsAvailable
        && _effects.Any(e =>
            e.Toggleable && e.Enabled != _savedEffectStates.GetValueOrDefault(e.FactoryType)
        );

    /// <summary>
    /// Resolves the effect registry, if any, and snapshots each effect's enabled state for
    /// change tracking.
    /// </summary>
    protected override void OnInitialized()
    {
        _effectRegistry = ServiceProvider.GetService<IEffectRegistry>();
        _effectsAvailable = _effectRegistry is not null;

        if (_effectsAvailable)
        {
            LoadEffects();
            SnapshotEffectState();
        }
    }

    // ── Effect helpers ──

    private void LoadEffects()
    {
        _effects = _effectRegistry!
            .GetAll()
            .Select(kvp =>
            {
                var factory = ServiceProvider.GetService(kvp.Key);
                var isConfigurable = factory is IConfigurableProviderFactory;

                return new EffectEntry
                {
                    FactoryType = kvp.Key,
                    Name = kvp.Key.Name,
                    FullName = kvp.Key.FullName ?? kvp.Key.Name,
                    Enabled = kvp.Value,
                    Toggleable = _effectRegistry.IsToggleable(kvp.Key),
                    IsConfigurable = isConfigurable,
                    Factory = factory,
                };
            })
            .OrderBy(e => e.Name)
            .ToList();
    }

    private void EnableAllEffects()
    {
        foreach (var entry in _effects.Where(e => e.Toggleable))
            entry.Enabled = true;
    }

    private void DisableAllEffects()
    {
        foreach (var entry in _effects.Where(e => e.Toggleable))
            entry.Enabled = false;
    }

    /// <summary>
    /// Applies the toggles the operator changed, then reads the registry again. A toggle left
    /// alone is not applied: applying it would put back the state this page loaded over a change
    /// another writer (another operator, the GraphQL <c>setEffectEnabled</c> mutation) made since.
    /// </summary>
    private void Save()
    {
        if (_effectRegistry is null)
            return;

        foreach (
            var entry in _effects.Where(e =>
                e.Toggleable && e.Enabled != _savedEffectStates.GetValueOrDefault(e.FactoryType)
            )
        )
        {
            if (entry.Enabled)
                _effectRegistry.Enable(entry.FactoryType);
            else
                _effectRegistry.Disable(entry.FactoryType);
        }

        ReloadEffects();

        NotificationService.Notify(
            new NotificationMessage
            {
                Severity = NotificationSeverity.Success,
                Summary = "Effects Saved",
                Detail = "Effect settings updated.",
                Duration = 4000,
            }
        );
    }

    /// <summary>Drops unsaved toggles and shows the registry's current state.</summary>
    private void DiscardChanges()
    {
        if (_effectRegistry is null)
            return;

        ReloadEffects();

        NotificationService.Notify(
            new NotificationMessage
            {
                Severity = NotificationSeverity.Info,
                Summary = "Changes Discarded",
                Detail = "The page shows the effects' current state.",
                Duration = 4000,
            }
        );
    }

    private void ReloadEffects()
    {
        LoadEffects();
        SnapshotEffectState();
    }

    private void SnapshotEffectState()
    {
        _savedEffectStates = _effects.ToDictionary(e => e.FactoryType, e => e.Enabled);
    }

    private async Task OpenConfigureDialog(EffectEntry entry)
    {
        if (entry.Factory is not IConfigurableProviderFactory configurable)
            return;

        await DialogService.OpenAsync<ConfigureEffectDialog>(
            $"Configure {entry.Name}",
            new Dictionary<string, object?>
            {
                ["ConfigurationType"] = configurable.GetConfigurationType(),
                ["Configuration"] = configurable.GetConfiguration(),
            },
            new DialogOptions
            {
                Width = "600px",
                Resizable = true,
                Draggable = true,
            }
        );
    }

    // ── Inner types ──

    private class EffectEntry
    {
        public required Type FactoryType { get; init; }
        public required string Name { get; init; }
        public required string FullName { get; init; }
        public bool Enabled { get; set; }
        public required bool Toggleable { get; init; }
        public required bool IsConfigurable { get; init; }
        public object? Factory { get; init; }
    }
}
