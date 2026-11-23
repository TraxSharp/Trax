#pragma warning disable CS1591
#pragma warning disable IDE0060
#pragma warning disable CA1019

namespace Trax.Analyzers.Annotations;

[System.Diagnostics.Conditional("TRAX_ANALYZERS_ANNOTATIONS")]
[System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class NonAwaitableTypeAttribute : System.Attribute
{
    public NonAwaitableTypeAttribute(System.Type type) { }
}
