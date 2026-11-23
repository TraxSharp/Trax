using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Trains.ManifestManager.Utilities;

/// <summary>
/// What bounds a run: its effective timeout (<see langword="null"/> when nothing bounds it) and
/// whether it, or the run it is nested in, is one a scheduler must leave alone.
/// </summary>
internal sealed record RunBound(TimeSpan? Timeout, bool Excluded, long? RootManifestId);
