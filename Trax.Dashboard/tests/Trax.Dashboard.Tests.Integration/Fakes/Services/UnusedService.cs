using System.Reflection;

namespace Trax.Dashboard.Tests.Integration.Fakes.Services;

/// <summary>
/// A stand-in for a service a page injects but the test never exercises. Any call fails the
/// test, so a stand-in cannot quietly make a test pass.
/// </summary>
public class UnusedService<T> : DispatchProxy
    where T : class
{
    public static T Create() => Create<T, UnusedService<T>>();

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        throw new InvalidOperationException(
            $"{typeof(T).Name}.{targetMethod?.Name} was called, but this test does not expect it."
        );
}
