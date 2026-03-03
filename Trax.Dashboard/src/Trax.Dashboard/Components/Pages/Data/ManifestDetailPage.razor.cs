using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Radzen;
using Trax.Dashboard.Components.Shared;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Metadata;
using Trax.Scheduler.Services.ManifestScheduler;
using static Trax.Dashboard.Utilities.DashboardFormatters;

namespace Trax.Dashboard.Components.Pages.Data;

public partial class ManifestDetailPage
{
    [Inject]
    private IDataContextProviderFactory DataContextFactory { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private IManifestScheduler ManifestScheduler { get; set; } = default!;

    [Inject]
    private NotificationService NotificationService { get; set; } = default!;

    [Parameter]
    public long ManifestId { get; set; }

    protected override object? GetRouteKey() => ManifestId;

    private Manifest? _manifest;
    private List<Metadata> _metadataItems = [];
    private List<Exclusion> _exclusions = [];
    private bool _triggering;
    private string? _triggerError;

    protected override async Task LoadDataAsync(CancellationToken cancellationToken)
    {
        using var context = await DataContextFactory.CreateDbContextAsync(cancellationToken);

        _manifest = await context
            .Manifests.Include(m => m.ManifestGroup)
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == ManifestId, cancellationToken);

        if (_manifest is not null)
        {
            _exclusions = _manifest.GetExclusions();

            _metadataItems = await context
                .Metadatas.AsNoTracking()
                .Where(m => m.ManifestId == ManifestId)
                .OrderByDescending(m => m.StartTime)
                .ToListAsync(cancellationToken);
        }
    }

    private static string FormatExclusion(Exclusion exclusion)
    {
        return exclusion.Type switch
        {
            ExclusionType.DaysOfWeek when exclusion.DaysOfWeek is not null => string.Join(
                ", ",
                exclusion.DaysOfWeek
            ),
            ExclusionType.Dates when exclusion.Dates is not null => string.Join(
                ", ",
                exclusion.Dates.Select(d => d.ToString("yyyy-MM-dd"))
            ),
            ExclusionType.DateRange =>
                $"{exclusion.StartDate?.ToString("yyyy-MM-dd")} to {exclusion.EndDate?.ToString("yyyy-MM-dd")}",
            ExclusionType.TimeWindow =>
                $"{exclusion.StartTime?.ToString("HH:mm")} to {exclusion.EndTime?.ToString("HH:mm")} daily",
            _ => "Unknown",
        };
    }

    private async Task TriggerManifest()
    {
        if (_manifest is null)
            return;

        _triggerError = null;
        _triggering = true;

        try
        {
            await ManifestScheduler.TriggerAsync(_manifest.ExternalId);

            NotificationService.Notify(
                NotificationSeverity.Success,
                "Train Queued",
                $"{ShortName(_manifest.Name)} has been queued for execution.",
                duration: 4000
            );
        }
        catch (Exception ex)
        {
            _triggerError = ex.Message;
        }
        finally
        {
            _triggering = false;
        }
    }
}
