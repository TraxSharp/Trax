using System.Diagnostics.CodeAnalysis;

// The CLI entry point in Program.cs is a top-level program that's never invoked
// during unit tests (tests target GenerateCommand.Handle directly). The synthesized
// Program class gets a partial declaration here so we can mark the auto-generated
// Main method as excluded from coverage rather than carrying a permanent 0%.
[ExcludeFromCodeCoverage]
internal partial class Program;
