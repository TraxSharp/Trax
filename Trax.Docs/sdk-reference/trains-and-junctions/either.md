---
title: Either and Unit
description: "Reference for Either, EitherStatus and Unit in Trax.Core.Functional: the two-track result Junctions() and RunEither return, and the empty value."
type: reference
area: trains
package: Trax.Core
namespace: Trax.Core.Functional
source:
  - Trax.Core/src/Trax.Core/Functional/Either.cs
  - Trax.Core/src/Trax.Core/Functional/Unit.cs
parent: Trains and Junctions
grand_parent: SDK Reference
nav_order: 9
---

# Either and Unit

`Either<TLeft, TRight>` is a value on one of two tracks: `Left`, which a train uses for the exception that switched it to the failure track, or `Right`, the result. `Junctions()` returns `Task<Either<Exception, TReturn>>` and [RunEither](/docs/sdk-reference/train-methods/run-either) hands it to the caller. `Unit` is the value of a junction or train that produces nothing.

## Signatures

```csharp fragment
namespace Trax.Core.Functional;

public enum EitherStatus : byte
{
    IsBottom = 0,
    IsLeft = 1,
    IsRight = 2,
}

public readonly struct Either<TLeft, TRight> : IEquatable<Either<TLeft, TRight>>
{
    public EitherStatus State { get; }
    public bool IsLeft { get; }
    public bool IsRight { get; }
    public bool IsBottom { get; }

    public static Either<TLeft, TRight> Left(TLeft value);
    public static Either<TLeft, TRight> Right(TRight value);
    public static implicit operator Either<TLeft, TRight>(TLeft value);
    public static implicit operator Either<TLeft, TRight>(TRight value);
    public static explicit operator TRight(Either<TLeft, TRight> either);
    public static explicit operator TLeft(Either<TLeft, TRight> either);

    public TResult Match<TResult>(Func<TRight, TResult> Right, Func<TLeft, TResult> Left, Func<TResult>? Bottom = null);
    public void Match(Action<TRight> Right, Action<TLeft> Left, Action? Bottom = null);
    public Unit IfRight(Action<TRight> action);
    public Unit IfLeft(Action<TLeft> action);
    public Either<TRight, TLeft> Swap();
    public TRight ValueUnsafe();
}

public readonly struct Unit : IComparable<Unit>, IEquatable<Unit>
{
    public static readonly Unit Default;
}
```

## Members

| Member | Description |
|--------|-------------|
| `State` | Which side holds the value: `EitherStatus.IsLeft`, `IsRight`, or `IsBottom` for `default(Either<TLeft, TRight>)`, which holds none |
| `IsLeft`, `IsRight`, `IsBottom` | `State` as a flag |
| `Left(value)`, `Right(value)` | An `Either` on that side. Throw `ArgumentNullException` for `null`: neither side holds null. |
| Implicit conversions | A `TLeft` or a `TRight` converts to an `Either`, so a junction chain can return either one where an `Either` is expected |
| Explicit conversions | `(TRight)either` and `(TLeft)either` read a side, and throw `InvalidCastException` when the value is on the other side |
| `Match(Right, Left, Bottom)` | Runs the function or action for the side that holds the value. Throws `InvalidOperationException` for a bottom value when no `Bottom` is given. |
| `IfRight(action)`, `IfLeft(action)` | Runs the action only when the value is on that side; returns `Unit` |
| `Swap()` | The same value on the other side. A bottom value stays bottom. |
| `ValueUnsafe()` | The right value, or `default` when the value is not on the right. Read the left value with `Swap().ValueUnsafe()`. |
| `Unit.Default` | The one `Unit` value. Every `Unit` equals every other. |

Two `Either` values are equal when they are on the same side and their values are equal. `ToString()` writes `Right(value)`, `Left(value)` or `Bottom`.

## Example

```csharp compile
using Trax.Core.Functional;

Either<Exception, int> result = 42;

var text = result.Match(Right: value => $"got {value}", Left: error => error.Message);

if (result.IsLeft)
    Console.WriteLine(result.Swap().ValueUnsafe().Message);
```

## See also

- [RunEither](/docs/sdk-reference/train-methods/run-either), which returns an `Either`
- [Resolve](/docs/sdk-reference/train-methods/resolve), which ends a chain in one
- [Functional programming in Trax](/docs/cross-cutting/functional-programming)
