---
title: Benchmarks
description: "BenchmarkDotNet measurements of train overhead against plain code, how it scales with junction count and memory, and how to run the suite."
type: reference
area: general
nav_order: 7
---

# Benchmarks

Measurements of what a train costs over plain code, taken with
[BenchmarkDotNet](https://benchmarkdotnet.org/). The suite is
`Trax.Effect/tests/Trax.Effect.Tests.Benchmarks/`. What the numbers mean for choosing where to use a
train is in [Train overhead](/docs/reference/train-overhead).

## Test Environment

```
BenchmarkDotNet v0.14.0, CachyOS
AMD Ryzen 7 7840U w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.103
  [Host] : .NET 10.0.3, X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```

## What's Being Measured

Three execution modes are compared for identical workloads. The suite also has a fourth,
`EffectTrain_InMemory_*` (a `ServiceTrain` with the in-memory data provider), which the tables
below do not report.

| Mode | Description |
|------|-------------|
| **Serial** | Plain function calls, no framework, no abstractions |
| **BaseTrain** | `Train<TIn, TOut>`, the core Chain/Resolve pipeline with `Either` error handling |
| **ServiceTrain (no effects)** | `ServiceTrain<TIn, TOut>`, full DI-resolved train with effect runner lifecycle, but no effect providers registered |

## Train Overhead

| Method | Mean | Allocated |
|--------|-----:|----------:|
| **Serial - Add 1** | 0.23 ns | - |
| BaseTrain - Add 1 | 1,564 ns | 3,688 B |
| ServiceTrain - Add 1 | 7,061 ns | 7,176 B |
| | | |
| **Serial - Add 3** (3 junctions) | 0.23 ns | - |
| BaseTrain - Add 3 | 1,966 ns | 4,536 B |
| ServiceTrain - Add 3 | 7,696 ns | 8,024 B |
| | | |
| **Serial - Transform** (DTO → entity) | 19.7 ns | 152 B |
| BaseTrain - Transform | 1,340 ns | 1,648 B |
| ServiceTrain - Transform | 6,889 ns | 5,232 B |
| | | |
| **Serial - Simulated I/O** (3× `Task.Yield`) | 1,053 ns | 112 B |
| BaseTrain - Simulated I/O | 3,747 ns | 4,992 B |
| ServiceTrain - Simulated I/O | 9,516 ns | 8,479 B |

## Scaling with Junction Count

| Junctions | Serial | BaseTrain | ServiceTrain | Base Overhead/Junction | Effect Overhead/Junction |
|------:|-------:|-------------:|---------------:|-------------------:|---------------------:|
| 1 | 4.7 ns | 1,630 ns | 7,186 ns | - | - |
| 3 | 4.7 ns | 1,987 ns | 7,622 ns | ~179 ns | ~218 ns |
| 5 | 5.1 ns | 2,468 ns | 8,079 ns | ~210 ns | ~223 ns |
| 10 | 10.7 ns | 3,440 ns | 9,016 ns | ~201 ns | ~203 ns |

Each additional junction adds roughly 200 ns in both train modes: junction instantiation, type mapping, and `Either` propagation through the chain.

### Memory Scaling

| Junctions | BaseTrain | ServiceTrain |
|------:|-------------:|---------------:|
| 1 | 3,688 B | 7,176 B |
| 3 | 4,536 B | 8,024 B |
| 5 | 5,384 B | 8,872 B |
| 10 | 7,720 B | 11,352 B |

Each additional junction allocates roughly 424 B (BaseTrain) or 464 B (ServiceTrain).

## Where the Overhead Comes From

| Source | Approximate Cost |
|--------|-----------------|
| `Train` base class instantiation + `Either` wrapping | ~1.3 μs |
| Per-junction: type resolution, `Chain<T>` dispatch, `Either` bind | ~200 ns/junction |
| DI scope creation (`CreateScope`) | ~1 μs |
| Effect runner lifecycle (initialize + save, no providers) | ~4.5 μs |

## Running the Benchmarks Yourself

From the repository root:

```bash
cd Trax.Effect/tests/Trax.Effect.Tests.Benchmarks

# Run all benchmarks
dotnet run -c Release -- --filter '*'

# Run a specific suite
dotnet run -c Release -- --filter '*TrainOverhead*'
dotnet run -c Release -- --filter '*Scaling*'

# List available benchmarks
dotnet run -c Release -- --list flat
```

Results vary by hardware; the ratios between the serial, base and service train modes vary less than the absolute numbers.
