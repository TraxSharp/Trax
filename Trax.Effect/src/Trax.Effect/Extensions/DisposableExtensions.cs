namespace Trax.Effect.Extensions;

using System;
using System.Reflection;

public static class DisposableExtensions
{
    // Looks for common boolean fields used by the dispose pattern.
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
