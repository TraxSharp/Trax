// What the examples on mediator/write-a-principal-provider.md take as given: the host's own
// record of which tenant the current work is for.
public static class TenantScope
{
    private static readonly AsyncLocal<string?> current = new();

    public static string? CurrentTenantId
    {
        get => current.Value;
        set => current.Value = value;
    }
}
