using Shopfront.Trains.PlaceOrder;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.TrainLifecycleHook;

namespace Shopfront.Staff;

// Tells staff about every failed run of the work nobody waits for. A failed order is the customer's.
public class StaffFailureHook(IStaffNotifier staff) : ITrainLifecycleHook
{
    public Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) =>
        metadata.Name == typeof(IPlaceOrderTrain).FullName
            ? Task.CompletedTask
            : staff.Notify($"{metadata.Name} failed: {exception.Message}", ct);
}
