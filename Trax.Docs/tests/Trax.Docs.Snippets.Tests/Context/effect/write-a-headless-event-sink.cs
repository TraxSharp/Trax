// What the compiled examples on effect/write-a-headless-event-sink.md take as given.
using Trax.Effect.Services.ServiceTrain;

public record GeocodeDriftReport(int Drifted);

public interface IGeocodeDriftRepository
{
    Task SaveAsync(GeocodeDriftReport report, CancellationToken ct);
}

public interface ICheckGeocodeDriftTrain : IServiceTrain<string, GeocodeDriftReport>;
