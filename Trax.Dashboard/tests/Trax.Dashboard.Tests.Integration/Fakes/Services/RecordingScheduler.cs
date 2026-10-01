using System.Reflection;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Dashboard.Tests.Integration.Fakes.Services;

/// <summary>
/// An <see cref="ITraxScheduler"/> that records every call and answers it from
/// <see cref="Respond"/>. A call the test gave no answer for fails, so a page that starts using
/// another scheduler method is noticed rather than silently served a default.
/// </summary>
public class RecordingScheduler : DispatchProxy
{
    /// <summary>Every call so far: the method name and its arguments.</summary>
    public List<(string Method, object?[] Args)> Calls { get; } = [];

    /// <summary>
    /// The answer to a call: the method name and its arguments in, the task to return out. Throw
    /// from it to make the call fail.
    /// </summary>
    public Func<string, object?[], object?> Respond { get; set; } =
        (method, _) =>
            throw new InvalidOperationException(
                $"ITraxScheduler.{method} was called, but the test did not expect it."
            );

    public static (ITraxScheduler Scheduler, RecordingScheduler Recorder) Create()
    {
        var scheduler = Create<ITraxScheduler, RecordingScheduler>();
        return (scheduler, (RecordingScheduler)(object)scheduler);
    }

    /// <summary>The arguments of each call to <paramref name="method"/>, in order.</summary>
    public IEnumerable<object?[]> CallsTo(string method) =>
        Calls.Where(c => c.Method == method).Select(c => c.Args);

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var name = targetMethod!.Name;
        args ??= [];
        lock (Calls)
            Calls.Add((name, args));
        return Respond(name, args);
    }
}
