namespace Trax.Effect.Services.LifecycleHookOutputPolicy;

/// <summary>
/// Marks a train whose output no lifecycle hook receives: <c>Metadata.Output</c> and <c>GetOutput&lt;T&gt;()</c> are
/// <c>null</c> while the hooks run, whatever <see cref="ILifecycleHookOutputPolicy"/> says, and the parameter effect
/// (<c>SaveTrainParameters</c>) stores none in <c>Metadata.Output</c>, whatever its configuration says. The run's row
/// is recorded as for any other train, without its output. The state-machine trains carry it: their output is an
/// instance's snapshot, which reaches its owner through the machine's own operations and feed, never through the
/// broadcaster, SignalR, another process's hooks or the operator's view of the run.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
internal sealed class OutputWithheldFromLifecycleHooksAttribute : Attribute;
