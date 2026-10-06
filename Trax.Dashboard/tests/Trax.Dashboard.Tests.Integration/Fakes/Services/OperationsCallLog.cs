using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Tests.Integration.Fakes.Services;

/// <summary>
/// The calls a page made to <see cref="IOperationsService"/>, and the answers a test gives in
/// place of the real service. A test reads <see cref="CallsTo"/> to see which service method a
/// page used and with what arguments.
/// </summary>
public class OperationsCallLog
{
    private readonly List<(string Method, object?[] Args)> _calls = [];

    /// <summary>
    /// Answers a call in place of the real service: the method name and its arguments in, the
    /// task to return out, or null to forward the call. Throw from it to make the call fail.
    /// </summary>
    public Func<string, object?[], object?>? Respond { get; set; }

    /// <summary>The arguments of each call to <paramref name="method"/>, in order.</summary>
    public IReadOnlyList<object?[]> CallsTo(string method)
    {
        lock (_calls)
            return _calls.Where(c => c.Method == method).Select(c => c.Args).ToList();
    }

    internal void Add(string method, object?[] args)
    {
        lock (_calls)
            _calls.Add((method, args));
    }
}
