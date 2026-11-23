#pragma warning disable CS1591
#pragma warning disable IDE0060
#pragma warning disable CA1019

namespace Trax.Analyzers.Annotations;

[System.Diagnostics.Conditional("TRAX_ANALYZERS_ANNOTATIONS")]
[System.AttributeUsage(System.AttributeTargets.Assembly | System.AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ExcludeFromCancellationTokenAnalysisAttribute : System.Attribute
{
    public ExcludeFromCancellationTokenAnalysisAttribute() { }

    public ExcludeFromCancellationTokenAnalysisAttribute(string documentationId) { }

    public ExcludeFromCancellationTokenAnalysisAttribute(System.Type containingType, string memberName) { }

    public ExcludeFromCancellationTokenAnalysisAttribute(System.Type containingType, string memberName, params System.Type[] parameterTypes) { }
}
