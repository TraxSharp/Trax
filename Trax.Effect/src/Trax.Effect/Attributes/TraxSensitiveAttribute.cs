namespace Trax.Effect.Attributes;

/// <summary>
/// Marks a property or field of a train's input or output (or of anything reachable from them)
/// whose value must not be written wherever Trax keeps a copy of that input or output.
/// </summary>
/// <remarks>
/// Masked in the stored input and output (<c>SaveTrainParameters</c>), in the junction output the
/// junction logger records, and in the output handed to lifecycle hooks, and so everywhere those
/// copies travel: the dashboard, the API's execution detail, logs and subscriptions. The value is
/// replaced by <c>{"_redacted": true}</c> under the same JSON name. The train itself always runs
/// with the real value; only the written copy is masked.
/// <para>
/// Marking is opt-in. Nothing is masked because of its name.
/// </para>
/// <para>
/// A marked property hides its whole value: an object is not walked, and a collection is replaced
/// whole. An unmarked property is walked, so a marked property on a nested object, or on each
/// element of a collection, is masked where it sits. On a positional record the attribute can be
/// written on the parameter, with or without <c>property:</c>. A mark on a base property, or on the
/// interface a class implements, applies to the override or implementation. Dictionary keys and
/// values have no member to mark and are not masked.
/// </para>
/// <para>
/// This does not touch the copy a train is <i>run</i> from: a queued entry's input
/// (<c>work_queue.input</c>) and a manifest's properties keep the real value, because the train
/// needs it. Keep a secret out of an input entirely where you can, and pass a reference to it
/// instead.
/// </para>
/// </remarks>
[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter,
    AllowMultiple = false,
    Inherited = true
)]
public sealed class TraxSensitiveAttribute : Attribute { }
