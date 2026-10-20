namespace Shopfront.Staff;

public interface IStaffNotifier
{
    Task Notify(string message, CancellationToken cancellationToken);
}
