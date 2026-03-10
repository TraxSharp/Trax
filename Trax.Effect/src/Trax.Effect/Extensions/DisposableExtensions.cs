namespace Trax.Effect.Extensions;

using System;
using System.Reflection;

public static class DisposableExtensions
{
    /// <summary>
    /// Checks whether the given <see cref="IDisposable"/> has been disposed by inspecting
    /// common dispose-pattern fields (<c>_disposed</c>, <c>disposed</c>, <c>disposedValue</c>, <c>isDisposed</c>).
    /// Returns <c>false</c> if no such field is found.
    /// </summary>
    /// <param name="disposable">The disposable instance to check.</param>
    /// <returns><c>true</c> if the object has been disposed; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="disposable"/> is <c>null</c>.</exception>
    public static bool IsDisposed(this IDisposable disposable)
    {
        if (disposable is null)
            throw new ArgumentNullException(nameof(disposable));

        var t = disposable.GetType();
        var field =
            t.GetField("_disposed", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? t.GetField("disposed", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? t.GetField("disposedValue", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? t.GetField("isDisposed", BindingFlags.NonPublic | BindingFlags.Instance);

        if (field is null)
            return false; // Assume not disposed if no marker is found.

        return field.GetValue(disposable) is bool b && b;
    }
}
