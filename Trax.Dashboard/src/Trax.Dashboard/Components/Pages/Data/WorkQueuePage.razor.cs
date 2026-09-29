using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Radzen;
using Trax.Dashboard.Components.Shared;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue;
using static Trax.Dashboard.Utilities.DashboardFormatters;

namespace Trax.Dashboard.Components.Pages.Data;

/// <summary>
/// The work queue list, at <c>/trax/data/work-queue</c>: queued, dispatched and cancelled entries,
/// paged from the database, with a batch action to cancel the selected queued entries.
/// Part of the dashboard UI, routed by the package; not intended to be used directly.
/// </summary>
public partial class WorkQueuePage;
