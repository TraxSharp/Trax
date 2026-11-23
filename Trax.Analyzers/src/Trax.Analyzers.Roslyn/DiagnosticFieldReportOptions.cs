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
internal enum DiagnosticFieldReportOptions
{
    None = 0x0,
    ReportOnReturnType = 0x1,
}
