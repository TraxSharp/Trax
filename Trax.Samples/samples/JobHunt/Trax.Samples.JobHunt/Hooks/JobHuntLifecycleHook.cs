using System.Text.Json;
using HotChocolate.Subscriptions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Samples.JobHunt.Subscriptions;
using Trax.Samples.JobHunt.Trains.AddJob;
using Trax.Samples.JobHunt.Trains.GenerateApplicationMaterials;
using Trax.Samples.JobHunt.Trains.MonitorJob;

namespace Trax.Samples.JobHunt.Hooks;

public class JobHuntLifecycleHook(ITopicEventSender eventSender) : ITrainLifecycleHook
{
    private static readonly Dictionary<string, string> TrainEventTypes = new()
    {
        [typeof(IAddJobTrain).FullName!] = "JobAdded",
        [typeof(IGenerateApplicationMaterialsTrain).FullName!] = "MaterialsGenerated",
        [typeof(IMonitorJobTrain).FullName!] = "JobMonitored",
    };

    private static readonly HashSet<string> TrackedTrains = TrainEventTypes.Keys.ToHashSet();

    public async Task OnCompleted(Metadata metadata, CancellationToken ct)
    {
        if (!TrackedTrains.Contains(metadata.Name) || metadata.Output is null)
            return;

        var eventType = TrainEventTypes[metadata.Name];
        string? userId = null;
        Guid? jobId = null;

        try
        {
            using var doc = JsonDocument.Parse(metadata.Output);
            var root = doc.RootElement;

            if (root.TryGetProperty("userId", out var u) || root.TryGetProperty("UserId", out u))
                userId = u.GetString();

            if (root.TryGetProperty("jobId", out var j) || root.TryGetProperty("JobId", out j))
                jobId = j.TryGetGuid(out var g) ? g : null;
        }
        catch (JsonException)
        {
            return;
        }

        var evt = new JobHuntSubscriptionEvent(
            EventType: eventType,
            Payload: metadata.Output,
            Timestamp: metadata.EndTime ?? DateTime.UtcNow,
            TrainExternalId: metadata.ExternalId,
            UserId: userId,
            JobId: jobId
        );

        if (userId is not null)
            await eventSender.SendAsync($"User:{userId}:jobs", evt, ct);

        if (jobId.HasValue)
        {
            var topic =
                eventType == "MaterialsGenerated"
                    ? $"Job:{jobId}:materials"
                    : $"Job:{jobId}:monitor";
            await eventSender.SendAsync(topic, evt, ct);
        }

        // Monitor changes also go to the user notification topic
        if (eventType == "JobMonitored" && userId is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(metadata.Output);
                if (
                    doc.RootElement.TryGetProperty("changed", out var changed)
                    || doc.RootElement.TryGetProperty("Changed", out changed)
                )
                {
                    if (changed.GetBoolean())
                        await eventSender.SendAsync($"User:{userId}:notifications", evt, ct);
                }
            }
            catch (JsonException)
            {
                // Ignore
            }
        }
    }
}
