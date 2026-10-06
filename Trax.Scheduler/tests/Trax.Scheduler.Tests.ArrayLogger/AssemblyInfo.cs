using System.Diagnostics.CodeAnalysis;

// This assembly is a test-only fake (an ILoggerProvider that captures log entries
// in memory for assertion-based testing). It's referenced by other test projects
// only — never shipped — so it has no business showing up in coverage totals.
[assembly: ExcludeFromCodeCoverage]
