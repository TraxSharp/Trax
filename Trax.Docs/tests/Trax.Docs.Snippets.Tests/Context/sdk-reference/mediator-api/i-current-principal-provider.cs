// What the example on sdk-reference/mediator-api/i-current-principal-provider.md takes as given:
// an ambient context that knows which tenant the current work is for.
public sealed class JobContext
{
    private static readonly AsyncLocal<JobContext?> current = new();

    public static JobContext? Current
    {
        get => current.Value;
        set => current.Value = value;
    }

    public required string TenantId { get; init; }
}
