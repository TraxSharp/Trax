using BenchmarkDotNet.Running;
using Trax.Effect.Tests.Benchmarks.Benchmarks;

// Usage (must run from tests/Trax.Effect.Tests.Benchmarks/):
//
// Run all benchmarks:
//   dotnet run -c Release -- --filter '*'
//
// Run a specific benchmark class:
//   dotnet run -c Release -- --filter '*TrainOverhead*'
//   dotnet run -c Release -- --filter '*Scaling*'
//
// Run a single benchmark method:
//   dotnet run -c Release -- --filter '*ScalingBenchmarks.BaseTrain*'
//
// List available benchmarks without running:
//   dotnet run -c Release -- --list flat
BenchmarkSwitcher.FromAssembly(typeof(TrainOverheadBenchmarks).Assembly).Run(args);
