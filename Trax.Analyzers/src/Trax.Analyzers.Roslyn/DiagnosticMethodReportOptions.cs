#if !TRAX_ANALYZERS_ROSLYN_ENABLE_WARNINGS
#pragma warning disable
#endif
#nullable enable
using System;

namespace Trax.Analyzers.Roslyn;

#if !TRAX_ANALYZERS_ROSLYN_DISABLE_EMBEDDEDATTRIBUTE
[Microsoft.CodeAnalysis.Embedded]
#endif
[Flags]
internal enum DiagnosticMethodReportOptions
{
    None = 0x0,
    ReportOnMethodName = 0x1,
    ReportOnReturnType = 0x2,
}
