namespace Trax.Effect.Attributes;

/// <summary>
/// Serves a state machine to clients through its own typed operations under the <c>machines</c> root: a
/// <c>start</c>, one mutation per event, <c>discard</c>, a <c>get</c> query and the the push (<c>&lt;machine&gt;Changed</c>)
/// subscription. It is to a machine what <see cref="TraxMutationAttribute"/> and <see cref="TraxQueryAttribute"/>
/// are to a train.
/// </summary>
/// <remarks>
/// Place it on the concrete <c>Machine&lt;TState, TTrigger&gt;</c> class. It is the only way to serve a machine, as
/// <see cref="TraxQueryAttribute"/> and <see cref="TraxMutationAttribute"/> are the only way to serve a train: a
/// machine without it has no client surface of its own, and there is no host-side switch that serves an unmarked one
/// or every machine. It is read from every machine the host registers, whether <c>AddStateMachines(...)</c> found it
/// in an assembly it scanned or the host registered it as <c>IMachine</c> itself. It is not inherited: a subclass of a
/// marked machine is served only if it is marked itself, and one registered unmarked refuses the host.
///
/// A marked machine is under the startup checks of a machine a client can reach: one built from delegates or
/// system-owned, or one that leaves an event, its start, a read or its discard without a rule, refuses the host.
/// The attribute on a type <c>AddStateMachines(...)</c> scans that is not a concrete machine (an abstract or open
/// generic machine, or a class that is no machine) refuses the host too, rather than serving nothing.
///
/// <example>
/// <code>
/// [TraxMachine]
/// public sealed class CheckoutMachine : Machine&lt;CheckoutState, CheckoutTrigger&gt; { ... }
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class TraxMachineAttribute : Attribute;
