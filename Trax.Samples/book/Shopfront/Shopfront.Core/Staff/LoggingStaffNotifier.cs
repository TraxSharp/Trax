namespace Shopfront.Staff;

// Writes each message to the log. A real shop sends it to a channel staff read.
public class LoggingStaffNotifier(ILogger<LoggingStaffNotifier> logger) : IStaffNotifier
{
    public Task Notify(string message, CancellationToken cancellationToken)
    {
        logger.LogWarning("For staff: {Message}", message);
        return Task.CompletedTask;
    }
}
