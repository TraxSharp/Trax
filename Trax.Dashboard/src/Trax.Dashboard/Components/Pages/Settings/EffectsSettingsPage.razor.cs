using Microsoft.AspNetCore.Components;
using Radzen;
using Trax.Dashboard.Components.Dialogs;
using Trax.Scheduler.Services.Effects;

namespace Trax.Dashboard.Components.Pages.Settings;

/// <summary>
/// The effects settings page, at <c>/trax/settings/effects</c>: lists every registered effect
/// provider, lets the user enable or disable the toggleable ones, and opens
/// <see cref="Dialogs.ConfigureEffectDialog"/> for configurable ones, all through
/// <see cref="IEffectSettingsService"/>, which the API's effects query and mutations call too.
/// Changes apply to this process in memory and are not persisted. Shows a notice instead when no effect registry is
/// registered. Part of the dashboard UI, routed by the package; not intended to be used directly.
/// </summary>
public partial class EffectsSettingsPage
{
    [Inject]
    private IEffectSettingsService EffectSettings { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [Inject]
    private DialogService DialogService { get; set; } = default!;

    // ── Effects state ──
    private bool _effectsAvailable;
    private List<EffectEntry> _effects = [];
    private Dictionary<string, bool> _savedEffectStates = new(StringComparer.Ordinal);

    // ── Dirty tracking ──
    private bool IsEffectsDirty =>
        _effectsAvailable
        && _effects.Any(e =>
            e.Toggleable && e.Enabled != _savedEffectStates.GetValueOrDefault(e.FullName)
        );

    /// <summary>
    /// Reads the effects through <see cref="IEffectSettingsService"/>, when an effect registry is
    /// registered, and snapshots each effect's enabled state for change tracking.
    /// </summary>
    protected override void OnInitialized()
    {
        _effectsAvailable = EffectSettings.IsAvailable;

        if (_effectsAvailable)
            ReloadEffects();
    }

    // ── Effect helpers ──

    private void LoadEffects()
    {
        _effects = EffectSettings
            .GetEffects()
            .Select(e => new EffectEntry
            {
                Name = e.Name,
                FullName = e.FullName,
                Enabled = e.Enabled,
                Toggleable = e.Toggleable,
                IsConfigurable = e.IsConfigurable,
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
    /// Applies the toggles the operator changed through
    /// <see cref="IEffectSettingsService.SetEffectEnabled"/>, the call the API's
    /// <c>setEffectEnabled</c> mutation makes, then reads the effects again. A toggle left alone
    /// is not applied: applying it would put back the state this page loaded over a change
    /// another writer (another operator, the mutation) made since. A toggle the service refuses
    /// is reported and the others are still applied.
    /// </summary>
    private void Save()
    {
        if (!_effectsAvailable)
            return;

        var refused = new List<string>();
        foreach (
            var entry in _effects.Where(e =>
                e.Toggleable && e.Enabled != _savedEffectStates.GetValueOrDefault(e.FullName)
            )
        )
        {
            var result = EffectSettings.SetEffectEnabled(entry.FullName, entry.Enabled);
            if (!result.Success)
                refused.Add(result.Message ?? $"{entry.Name} was not changed.");
        }

        ReloadEffects();

        NotificationService.Notify(
            refused.Count == 0
                ? new NotificationMessage
                {
                    Severity = NotificationSeverity.Success,
                    Summary = "Effects Saved",
                    Detail = "Effect settings updated.",
                    Duration = 4000,
                }
                : new NotificationMessage
                {
                    Severity = NotificationSeverity.Error,
                    Summary = "Some Effects Not Saved",
                    Detail = string.Join(" ", refused),
                    Duration = 8000,
                }
        );
    }

    /// <summary>Drops unsaved toggles and shows the effects' current state.</summary>
    private void DiscardChanges()
    {
        if (!_effectsAvailable)
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
        _savedEffectStates = _effects.ToDictionary(
            e => e.FullName,
            e => e.Enabled,
            StringComparer.Ordinal
        );
    }

    private async Task OpenConfigureDialog(EffectEntry entry)
    {
        if (!entry.IsConfigurable)
            return;

        await DialogService.OpenAsync<ConfigureEffectDialog>(
            $"Configure {entry.Name}",
            new Dictionary<string, object?>
            {
                [nameof(ConfigureEffectDialog.EffectFullName)] = entry.FullName,
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
        public required string Name { get; init; }
        public required string FullName { get; init; }
        public bool Enabled { get; set; }
        public required bool Toggleable { get; init; }
        public required bool IsConfigurable { get; init; }
    }
}
