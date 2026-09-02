using System.Text.Json;
using FluentAssertions;
using HotChocolate;
using HotChocolate.Execution;

namespace Trax.Api.Tests;

/// <summary>
/// Shorthand for reaching into a HotChocolate execution result from a test.
/// </summary>
internal static class GraphQLExecutionResultExtensions
{
    /// <summary>
    /// Narrows an <see cref="IExecutionResult"/> to the single-operation result, failing
    /// the test with a clear message when the request produced a stream instead.
    /// </summary>
    public static OperationResult ExpectOperationResult(this IExecutionResult result)
    {
        result.Should().BeAssignableTo<OperationResult>();
        return (OperationResult)result;
    }

    /// <summary>
    /// The result's <c>data</c> as a plain nested map: objects become
    /// <see cref="IReadOnlyDictionary{TKey,TValue}"/>, lists become
    /// <see cref="IReadOnlyList{T}"/>, and scalars become <c>string</c>, <c>long</c>,
    /// <c>double</c>, <c>bool</c> or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// HotChocolate 16 materialises results into an internal result document rather than
    /// the object graph the old <c>IOperationResult.Data</c> exposed, so the map is rebuilt
    /// from the serialized payload. Going through JSON also keeps assertions independent of
    /// whatever HotChocolate materialises into next.
    /// </remarks>
    public static IReadOnlyDictionary<string, object?> DataMap(this OperationResult result)
    {
        using var document = JsonDocument.Parse(result.ToJson());

        document
            .RootElement.TryGetProperty("data", out var data)
            .Should()
            .BeTrue("the operation should have produced data");

        data.ValueKind.Should().Be(JsonValueKind.Object);

        return (IReadOnlyDictionary<string, object?>)ToClrValue(data)!;
    }

    private static object? ToClrValue(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.Object => element
                .EnumerateObject()
                .ToDictionary(p => p.Name, p => ToClrValue(p.Value), StringComparer.Ordinal),
            JsonValueKind.Array => element.EnumerateArray().Select(ToClrValue).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var i) ? i : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
}
