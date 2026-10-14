// What the custom submitter on scheduler/job-submission.md sends its jobs to.
public interface IExternalJobQueue
{
    Task<string> SendAsync(
        long metadataId,
        object? input,
        int priority,
        CancellationToken cancellationToken
    );
}
