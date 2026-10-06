using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Trax.Dashboard.Tests.Integration.Fakes.Data;

/// <summary>
/// Counts the entities EF Core builds from query results, by type, so a test can tell a page
/// that reads one page of rows from one that reads them all.
/// </summary>
public sealed class MaterializationCounter : IMaterializationInterceptor
{
    private readonly Dictionary<Type, int> _counts = [];

    public int CountOf<T>()
    {
        lock (_counts)
            return _counts.GetValueOrDefault(typeof(T));
    }

    public void Reset()
    {
        lock (_counts)
            _counts.Clear();
    }

    public object InitializedInstance(MaterializationInterceptionData data, object entity)
    {
        lock (_counts)
            _counts[entity.GetType()] = _counts.GetValueOrDefault(entity.GetType()) + 1;
        return entity;
    }
}
