---
layout: default
title: Benchmarks
parent: Reference
nav_order: 2
---

# Benchmarks

Trax.Core adds overhead to every train invocation. This page presents honest numbers so you can make informed decisions about where it's appropriate to use.

All benchmarks use [BenchmarkDotNet](https://benchmarkdotnet.org/) and are located in `tests/Trax.Core.Tests.Benchmarks/`.

## Test Environment

```
BenchmarkDotNet v0.14.0, CachyOS
AMD Ryzen 7 7840U w/ Radeon 780M Graphics, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.103
  [Host] : .NET 10.0.3, X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
```

## What's Being Measured

Three execution modes are compared for identical workloads:

| Mode | Description |
|------|-------------|
| **Serial** | Plain function calls — no framework, no abstractions |
| **BaseTrain** | `Train<TIn, TOut>` — the core Chain/Resolve pipeline with `Either` error handling |
| **ServiceTrain (no effects)** | `ServiceTrain<TIn, TOut>` — full DI-resolved train with effect runner lifecycle, but no effect providers registered |

## Train Overhead

How much does Trax.Core cost for different kinds of work?

| Method | Mean | Allocated |
|--------|-----:|----------:|
| **Serial — Add 1** | 0.23 ns | — |
| BaseTrain — Add 1 | 1,564 ns | 3,688 B |
| ServiceTrain — Add 1 | 7,061 ns | 7,176 B |
| | | |
| **Serial — Add 3** (3 steps) | 0.23 ns | — |
| BaseTrain — Add 3 | 1,966 ns | 4,536 B |
| ServiceTrain — Add 3 | 7,696 ns | 8,024 B |
| | | |
| **Serial — Transform** (DTO → entity) | 19.7 ns | 152 B |
| BaseTrain — Transform | 1,340 ns | 1,648 B |
| ServiceTrain — Transform | 6,889 ns | 5,232 B |
| | | |
| **Serial — Simulated I/O** (3× `Task.Yield`) | 1,053 ns | 112 B |
| BaseTrain — Simulated I/O | 3,747 ns | 4,992 B |
| ServiceTrain — Simulated I/O | 9,516 ns | 8,479 B |

### Reading the Numbers

For trivial arithmetic, the framework overhead looks enormous in relative terms — a `BaseTrain` is ~6,900× slower than `input + 1`. But that comparison is misleading because `input + 1` completes in a fraction of a nanosecond.

In absolute terms:

- **BaseTrain** adds roughly **1.5 μs** of fixed overhead per invocation.
- **ServiceTrain** (no effects) adds roughly **7 μs** per invocation, covering DI scope creation and effect runner lifecycle.

Once the steps do real work, the overhead shrinks dramatically. With simulated I/O (`Task.Yield`), the BaseTrain is only **3.6×** the serial cost instead of 6,900×.

For a train step that makes a database call (~1–10 ms) or an HTTP request (~50–500 ms), the 1.5–7 μs framework overhead is **less than 0.01%** of total execution time.

## Scaling with Step Count

How does overhead grow as you chain more steps?

| Steps | Serial | BaseTrain | ServiceTrain | Base Overhead/Step | Effect Overhead/Step |
|------:|-------:|-------------:|---------------:|-------------------:|---------------------:|
| 1 | 4.7 ns | 1,630 ns | 7,186 ns | — | — |
| 3 | 4.7 ns | 1,987 ns | 7,622 ns | ~179 ns | ~218 ns |
| 5 | 5.1 ns | 2,468 ns | 8,079 ns | ~210 ns | ~223 ns |
| 10 | 10.7 ns | 3,440 ns | 9,016 ns | ~201 ns | ~203 ns |

Each additional step adds roughly **200 ns** of overhead in both train modes. This covers step instantiation, type mapping, and `Either` propagation through the chain.

### Memory Scaling

| Steps | BaseTrain | ServiceTrain |
|------:|-------------:|---------------:|
| 1 | 3,688 B | 7,176 B |
| 3 | 4,536 B | 8,024 B |
| 5 | 5,384 B | 8,872 B |
| 10 | 7,720 B | 11,352 B |

Each additional step allocates roughly **~424 B** (BaseTrain) or **~464 B** (ServiceTrain).

## Where the Overhead Comes From

| Source | Approximate Cost |
|--------|-----------------|
| `Train` base class instantiation + `Either` wrapping | ~1.3 μs |
| Per-step: type resolution, `Chain<T>` dispatch, `Either` bind | ~200 ns/step |
| DI scope creation (`CreateScope`) | ~1 μs |
| Effect runner lifecycle (initialize + save, no providers) | ~4.5 μs |

## Guidance

**Trax.Core is not designed for hot-path, sub-microsecond operations.** It's designed for business train orchestration where each step does meaningful work — database queries, API calls, file I/O, domain logic.

Use Trax.Core when:
- Steps perform I/O or non-trivial computation (the ~7 μs overhead is noise)
- You value error propagation, observability, and composability over raw throughput
- You're building trains that run at request-level granularity (tens to hundreds per second), not tight inner loops

Don't use Trax.Core for:
- Per-element processing in large collections (use LINQ or loops)
- Anything that needs to run millions of times per second
- Pure computation where every nanosecond matters

## Running the Benchmarks Yourself

```bash
cd tests/Trax.Core.Tests.Benchmarks/

# Run all benchmarks
dotnet run -c Release -- --filter '*'

# Run a specific suite
dotnet run -c Release -- --filter '*TrainOverhead*'
dotnet run -c Release -- --filter '*Scaling*'

# List available benchmarks
dotnet run -c Release -- --list flat
```

{: .note }
Results will vary by hardware. The relative ratios between serial, base, and effect trains are more meaningful than the absolute numbers.
