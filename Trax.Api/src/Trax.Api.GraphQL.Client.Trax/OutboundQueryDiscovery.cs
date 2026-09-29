using System.Reflection;

namespace Trax.Api.GraphQL.Client.Trax;

/// <summary>
/// Walks an assembly for <see cref="TraxOutboundQueryAttribute"/>-decorated request types and
/// returns a flat mapping of (request type -> endpoint name), for tooling that reports which
/// external endpoints an app calls. Nothing in Trax calls it today.
/// </summary>
public static class OutboundQueryDiscovery
{
    /// <summary>One request type marked with <see cref="TraxOutboundQueryAttribute"/>.</summary>
    /// <param name="RequestType">The request type.</param>
    /// <param name="Endpoint">The logical endpoint name from the attribute.</param>
    /// <param name="QueryName">The operation name parsed from the request's query, or <c>null</c> when it is anonymous, not a query or mutation, or cannot be read.</param>
    public sealed record Entry(Type RequestType, string Endpoint, string? QueryName);

    /// <summary>
    /// Returns every concrete request type in <paramref name="assemblies"/> that carries
    /// <see cref="TraxOutboundQueryAttribute"/>. Request types are created without running a
    /// constructor to read their query.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan.</param>
    /// <exception cref="ArgumentNullException"><paramref name="assemblies"/> is <c>null</c>.</exception>
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
