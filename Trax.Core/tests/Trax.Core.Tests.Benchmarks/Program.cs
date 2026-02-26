using BenchmarkDotNet.Running;
using Trax.Core.Tests.Benchmarks.Benchmarks;

// Usage (must run from tests/Trax.Core.Tests.Benchmarks/):
//
// Run all benchmarks:
//   dotnet run -c Release -- --filter '*'
//
// Run a specific benchmark class:
//   dotnet run -c Release -- --filter '*WorkflowOverhead*'
//   dotnet run -c Release -- --filter '*Scaling*'
//
// Run a single benchmark method:
//   dotnet run -c Release -- --filter '*ScalingBenchmarks.BaseWorkflow*'
//
// List available benchmarks without running:
//   dotnet run -c Release -- --list flat
BenchmarkSwitcher.FromAssembly(typeof(WorkflowOverheadBenchmarks).Assembly).Run(args);
