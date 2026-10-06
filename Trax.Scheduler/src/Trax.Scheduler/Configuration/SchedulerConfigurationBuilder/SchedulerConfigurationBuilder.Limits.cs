using Trax.Scheduler.Services.Operations;

namespace Trax.Scheduler.Configuration;

public partial class SchedulerConfigurationBuilder
{
    /// <summary>
    /// Refuses a duration or count set in code that the scheduler cannot run with, against the
    /// same <see cref="SchedulerConfigLimits"/> the operations service holds a runtime change to.
    /// Each problem names the builder method (or options property) that set the value.
    /// </summary>
    /// <exception cref="InvalidOperationException">A value is outside its range.</exception>
    private void ValidateLimits()
    {
        var problems = new[]
        {
            SchedulerConfigLimits.TimerInterval(
                _configuration.ManifestManagerPollingInterval,
                _manifestManagerIntervalSetBy
            ),
            SchedulerConfigLimits.TimerInterval(
                _configuration.JobDispatcherPollingInterval,
                _jobDispatcherIntervalSetBy
            ),
            SchedulerConfigLimits.NotNegative(
                _configuration.DefaultMaxRetries,
                nameof(DefaultMaxRetries)
            ),
            SchedulerConfigLimits.NonNegativeDuration(
                _configuration.DeadLetterRetentionPeriod,
                nameof(DeadLetterRetentionPeriod)
            ),
            SchedulerConfigLimits.AtLeastOne(_configuration.MaxActiveJobs, nameof(MaxActiveJobs)),
            SchedulerConfigLimits.NonNegativeDuration(
                _configuration.DefaultRetryDelay,
                nameof(DefaultRetryDelay)
            ),
            SchedulerConfigLimits.BackoffMultiplier(
                _configuration.RetryBackoffMultiplier,
                nameof(RetryBackoffMultiplier)
            ),
            SchedulerConfigLimits.NonNegativeDuration(
                _configuration.MaxRetryDelay,
                nameof(MaxRetryDelay)
            ),
            SchedulerConfigLimits.PositiveDuration(
                _configuration.DefaultJobTimeout,
                nameof(DefaultJobTimeout)
            ),
            SchedulerConfigLimits.PositiveDuration(
                _configuration.StalePendingTimeout,
                nameof(StalePendingTimeout)
            ),
            SchedulerConfigLimits.PositiveDuration(
                _configuration.StaleInProgressTimeout,
                nameof(StaleInProgressTimeout)
            ),
            SchedulerConfigLimits.PositiveDuration(
                _configuration.StaleStagedEntryTimeout,
                nameof(StaleStagedEntryTimeout)
            ),
            SchedulerConfigLimits.NonNegativeDuration(
                _configuration.DefaultMisfireThreshold,
                nameof(DefaultMisfireThreshold)
            ),
            SchedulerConfigLimits.PositiveDuration(
                _configuration.SchedulerLivenessThreshold,
                nameof(SchedulerLivenessThreshold)
            ),
        }
            .Concat(MetadataCleanupProblems(_configuration.MetadataCleanup))
            .Concat(_localWorkerOptions.Problems(nameof(ConfigureLocalWorkers)))
            .OfType<string>()
            .ToList();

        if (problems.Count > 0)
            throw new InvalidOperationException(
                "The scheduler configuration has values it cannot run with. "
                    + string.Join(" ", problems)
            );
    }

    private static IEnumerable<string?> MetadataCleanupProblems(
        MetadataCleanupConfiguration? cleanup
    )
    {
        if (cleanup is null)
            yield break;

        const string method = nameof(AddMetadataCleanup);

        yield return SchedulerConfigLimits.PositiveDuration(
            cleanup.RetentionPeriod,
            $"{method}: {nameof(MetadataCleanupConfiguration.RetentionPeriod)}"
        );
        yield return SchedulerConfigLimits.TimerInterval(
            cleanup.CleanupInterval,
            $"{method}: {nameof(MetadataCleanupConfiguration.CleanupInterval)}"
        );
        yield return SchedulerConfigLimits.DeleteBatchSize(
            cleanup.DeleteBatchSize,
            $"{method}: {nameof(MetadataCleanupConfiguration.DeleteBatchSize)}"
        );

        foreach (var (name, retention) in cleanup.TrainTypeRetentions)
            yield return SchedulerConfigLimits.PositiveDuration(
                retention,
                $"{method}: the retention of '{name}'"
            );
    }
}
