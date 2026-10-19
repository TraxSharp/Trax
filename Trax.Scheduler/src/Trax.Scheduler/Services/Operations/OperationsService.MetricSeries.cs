using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Metadata = Trax.Effect.Models.Metadata.Metadata;

namespace Trax.Scheduler.Services.Operations;

// Runs over time for one train, manifest or group (operations.metrics.series). Counts and the
// duration histogram are grouped in the database; the percentiles are read off the histogram.
public partial class OperationsService
{
    /// <summary>
    /// The width of one duration bin: a run of <c>d</c> milliseconds falls in bin
    /// <c>floor(ln(d + 1) * DurationBinsPerE)</c>, so neighbouring bins differ by about 13%.
    /// </summary>
    private const double DurationBinsPerE = 8;

    /// <summary>
    /// Why <paramref name="scope"/> refuses <paramref name="range"/>, or null when it serves it.
    /// </summary>
    /// <remarks>
    /// Measured on the Trax.Api stress fixture (3,000,000 runs, plus a million in the one train,
    /// manifest and group read): the 7-day series of those runs (about 230,000 of them) takes 170 to 340 ms, and the
    /// 30-day one 560 to 660 ms for every scope, over the 450 ms the dashboard metrics budget
    /// allows. The 30-day series reads every run of 30 days through
    /// <c>ix_metadata_metrics_state_time</c> (a train) or <c>ix_metadata_manifest_state</c> (a
    /// manifest or group), and no existing index pre-aggregates them, so it is refused for every
    /// scope rather than served slowly.
    /// </remarks>
    public static string? MetricSeriesRefusal(MetricSeriesScope scope, MetricSeriesRange range) =>
        range == MetricSeriesRange.Last30Days
            ? "A 30-day series is not served: at a million runs it reads past the metrics budget "
                + "on the existing indexes. Read the last 7 days instead."
            : null;

    /// <summary>The bucket size and count of each range.</summary>
    internal static (TimeSpan Size, int Count) MetricSeriesBuckets(MetricSeriesRange range) =>
        range switch
        {
            MetricSeriesRange.LastHour => (TimeSpan.FromMinutes(1), 60),
            MetricSeriesRange.Last24Hours => (TimeSpan.FromMinutes(15), 96),
            MetricSeriesRange.Last7Days => (TimeSpan.FromHours(1), 168),
            MetricSeriesRange.Last30Days => (TimeSpan.FromHours(6), 120),
            _ => throw new ArgumentOutOfRangeException(nameof(range), range, null),
        };

    /// <inheritdoc />
    public async Task<MetricSeries> GetMetricSeriesAsync(
        MetricSeriesScope scope,
        long? id,
        string? name,
        MetricSeriesRange range,
        CancellationToken ct
    )
    {
        if (scope == MetricSeriesScope.Train && string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A train's series needs the train's name.", nameof(name));
        if (scope != MetricSeriesScope.Train && id is null)
            throw new ArgumentException(
                $"A {scope.ToString().ToLowerInvariant()}'s series needs its id.",
                nameof(id)
            );
        if (MetricSeriesRefusal(scope, range) is { } refusal)
            throw new ArgumentException(refusal, nameof(range));

        var (size, count) = MetricSeriesBuckets(range);
        var now = DateTime.UtcNow;
        var to = new DateTime(now.Ticks - now.Ticks % size.Ticks, DateTimeKind.Utc) + size;
        var from = to - TimeSpan.FromTicks(size.Ticks * count);

        // Each bucket size divides an hour or a day, so a bucket is a day, an hour of it divided
        // by hourDiv, and a minute of that divided by minuteDiv.
        var hourDiv = size >= TimeSpan.FromHours(1) ? (int)size.TotalHours : 1;
        var minuteDiv = size >= TimeSpan.FromHours(1) ? 60 : (int)size.TotalMinutes;

        IQueryable<Metadata> Scoped(IDataContext db)
        {
            var runs = db
                .Metadatas.AsNoTracking()
                .Where(m => m.StartTime >= from && m.StartTime < to);
            return scope switch
            {
                MetricSeriesScope.Train => runs.Where(m => m.Name == name),
                MetricSeriesScope.Manifest => runs.Where(m => m.ManifestId == id),
                _ => runs.Where(m =>
                    m.ManifestId != null
                    && db.Manifests.Any(mf => mf.Id == m.ManifestId && mf.ManifestGroupId == id)
                ),
            };
        }

        async Task<T> Read<T>(Func<IDataContext, Task<T>> read)
        {
            using var db = await _dataContextFactory.CreateDbContextAsync(ct);
            return await read(db);
        }

        // Counts and the histogram read different index ranges; read them at once.
        var countsRead = Read(db =>
            Scoped(db)
                .Where(m =>
                    m.TrainState == TrainState.Completed
                    || m.TrainState == TrainState.Failed
                    || m.TrainState == TrainState.Cancelled
                )
                .GroupBy(m => new
                {
                    m.StartTime.Date,
                    Hour = m.StartTime.Hour / hourDiv,
                    Minute = m.StartTime.Minute / minuteDiv,
                    m.TrainState,
                })
                .Select(g => new
                {
                    g.Key.Date,
                    g.Key.Hour,
                    g.Key.Minute,
                    g.Key.TrainState,
                    Count = g.Count(),
                })
                .ToListAsync(ct)
        );
        var histogramRead = Read(db =>
            ReadDurationHistogramAsync(
                db,
                Scoped(db).Where(m => m.TrainState == TrainState.Completed && m.EndTime != null),
                hourDiv,
                minuteDiv,
                ct
            )
        );
        await Task.WhenAll(countsRead, histogramRead);

        DateTime BucketOf(DateTime date, int hour, int minute) =>
            DateTime.SpecifyKind(
                date.Date.AddHours(hour * hourDiv).AddMinutes(minute * minuteDiv),
                DateTimeKind.Utc
            );

        var counts = (await countsRead)
            .GroupBy(x => (BucketOf(x.Date, x.Hour, x.Minute), x.TrainState))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Count));
        var histograms = (await histogramRead)
            .GroupBy(x => BucketOf(x.Date, x.Hour, x.Minute))
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Bin).ToList());

        int CountOf(DateTime bucket, TrainState state) =>
            counts.TryGetValue((bucket, state), out var c) ? c : 0;

        var buckets = Enumerable
            .Range(0, count)
            .Select(i =>
            {
                var start = from + TimeSpan.FromTicks(size.Ticks * i);
                var bins = histograms.TryGetValue(start, out var b) ? b : null;
                return new MetricSeriesBucket(
                    start,
                    CountOf(start, TrainState.Completed),
                    CountOf(start, TrainState.Failed),
                    CountOf(start, TrainState.Cancelled),
                    bins is null ? null : Percentile(bins, 0.50),
                    bins is null ? null : Percentile(bins, 0.95)
                );
            })
            .ToList();

        return new MetricSeries(scope, range, from, to, (int)size.TotalSeconds, buckets);
    }

    /// <summary>One duration bin of one bucket: how many runs fell in it and the shortest and longest.</summary>
    private sealed record DurationBin(
        DateTime Date,
        int Hour,
        int Minute,
        int Bin,
        int Count,
        double MinMs,
        double MaxMs
    );

    /// <summary>
    /// The completed runs' durations, binned on a log scale per bucket, in the database. The
    /// duration is written the way the provider translates it, as for the dashboard's average
    /// durations (see <see cref="AveragesDurationFromTicks"/>).
    /// </summary>
    private static async Task<List<DurationBin>> ReadDurationHistogramAsync(
        IDataContext db,
        IQueryable<Metadata> runs,
        int hourDiv,
        int minuteDiv,
        CancellationToken ct
    )
    {
        var byTimeSpan = runs.Select(m => new
            {
                m.StartTime.Date,
                Hour = m.StartTime.Hour / hourDiv,
                Minute = m.StartTime.Minute / minuteDiv,
                Ms = (m.EndTime!.Value - m.StartTime).TotalMilliseconds,
            })
            .Select(x => new
            {
                x.Date,
                x.Hour,
                x.Minute,
                x.Ms,
                Bin = (int)Math.Floor(Math.Log(Math.Max(x.Ms, 0) + 1) * DurationBinsPerE),
            })
            .GroupBy(x => new
            {
                x.Date,
                x.Hour,
                x.Minute,
                x.Bin,
            })
            .Select(g => new
            {
                g.Key.Date,
                g.Key.Hour,
                g.Key.Minute,
                g.Key.Bin,
                Count = g.Count(),
                MinMs = g.Min(x => x.Ms),
                MaxMs = g.Max(x => x.Ms),
            });

        var fromTicks = AveragesDurationFromTicks.GetOrAdd(
            db.GetType(),
            _ => !Translates(byTimeSpan)
        );

        if (!fromTicks)
            return (await byTimeSpan.ToListAsync(ct))
                .Select(x => new DurationBin(
                    x.Date,
                    x.Hour,
                    x.Minute,
                    x.Bin,
                    x.Count,
                    x.MinMs,
                    x.MaxMs
                ))
                .ToList();

        const double ticksPerMs = TimeSpan.TicksPerMillisecond;
        return (
            await runs.Select(m => new
                {
                    m.StartTime.Date,
                    Hour = m.StartTime.Hour / hourDiv,
                    Minute = m.StartTime.Minute / minuteDiv,
                    Ms = (m.EndTime!.Value.Ticks - m.StartTime.Ticks) / ticksPerMs,
                })
                .Select(x => new
                {
                    x.Date,
                    x.Hour,
                    x.Minute,
                    x.Ms,
                    Bin = (int)Math.Floor(Math.Log(Math.Max(x.Ms, 0) + 1) * DurationBinsPerE),
                })
                .GroupBy(x => new
                {
                    x.Date,
                    x.Hour,
                    x.Minute,
                    x.Bin,
                })
                .Select(g => new
                {
                    g.Key.Date,
                    g.Key.Hour,
                    g.Key.Minute,
                    g.Key.Bin,
                    Count = g.Count(),
                    MinMs = g.Min(x => x.Ms),
                    MaxMs = g.Max(x => x.Ms),
                })
                .ToListAsync(ct)
        ).Select(x => new DurationBin(x.Date, x.Hour, x.Minute, x.Bin, x.Count, x.MinMs, x.MaxMs)).ToList();
    }

    /// <summary>
    /// The <paramref name="p"/> quantile of the durations <paramref name="bins"/> hold, in order.
    /// It finds the bin holding the quantile's rank and places it between that bin's shortest and
    /// longest run by its position among the bin's runs, so it is exact when the bin holds one
    /// duration and never outside the durations recorded.
    /// </summary>
    internal static double Percentile(
        IReadOnlyList<(int Count, double MinMs, double MaxMs)> bins,
        double p
    )
    {
        var total = bins.Sum(b => b.Count);
        var rank = p * (total - 1);
        var before = 0;
        foreach (var (binCount, min, max) in bins)
        {
            if (rank < before + binCount)
                return binCount <= 1 ? min : min + (max - min) * (rank - before) / (binCount - 1);
            before += binCount;
        }
        return bins[^1].MaxMs;
    }

    private static double Percentile(List<DurationBin> bins, double p) =>
        Percentile(bins.Select(b => (b.Count, b.MinMs, b.MaxMs)).ToList(), p);
}
