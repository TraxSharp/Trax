namespace Trax.Dashboard.Utilities;

/// <summary>
/// Links to a persisted operation's detail page. A row is keyed by its tenant and its id, so
/// the link carries both: the id in the path and a non-default tenant as <c>?tenant=</c>.
/// </summary>
internal static class PersistedOperationRoutes
{
    public const string TenantQueryParameter = "tenant";

    public static string Detail(string id, string? tenantKey)
    {
        var path = $"trax/data/persisted-operations/{Uri.EscapeDataString(id)}";
        return string.IsNullOrEmpty(tenantKey)
            ? path
            : $"{path}?{TenantQueryParameter}={Uri.EscapeDataString(tenantKey)}";
    }
}
