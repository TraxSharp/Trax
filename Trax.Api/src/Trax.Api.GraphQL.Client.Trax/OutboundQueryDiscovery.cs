using System.Reflection;

namespace Trax.Api.GraphQL.Client.Trax;

/// <summary>
/// Walks an assembly for <see cref="TraxOutboundQueryAttribute"/>-decorated request types and
/// returns a flat mapping of (request type -> endpoint name). Used by dashboard discovery; the
/// same data can be exported as JSON for tooling.
/// </summary>
public static class OutboundQueryDiscovery
{
    public sealed record Entry(Type RequestType, string Endpoint, string? QueryName);

    public static IReadOnlyList<Entry> Discover(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var results = new List<Entry>();
        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (!typeof(IGenericGraphQLClientRequest).IsAssignableFrom(type))
                    continue;
                if (type.IsAbstract || !type.IsClass)
                    continue;

                var attr = type.GetCustomAttribute<TraxOutboundQueryAttribute>();
                if (attr is null)
                    continue;

                results.Add(new Entry(type, attr.Endpoint, ExtractOperationName(type)));
            }
        }
        return results;
    }

    private static string? ExtractOperationName(Type type)
    {
        try
        {
            var instance = (IGenericGraphQLClientRequest)
                System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
            var query = instance.Query;
            // Parse: "query NAME(..." or "mutation NAME(..." - first identifier after the keyword.
            var trimmed = query.TrimStart();
            string? keyword = null;
            if (trimmed.StartsWith("query", StringComparison.Ordinal))
                keyword = "query";
            else if (trimmed.StartsWith("mutation", StringComparison.Ordinal))
                keyword = "mutation";
            if (keyword is null)
                return null;

            var rest = trimmed[keyword.Length..].TrimStart();
            if (rest.Length == 0 || rest[0] == '{' || rest[0] == '(')
                return null;

            var end = 0;
            while (end < rest.Length && (char.IsLetterOrDigit(rest[end]) || rest[end] == '_'))
                end++;
            return end == 0 ? null : rest[..end];
        }
        catch
        {
            return null;
        }
    }
}
