namespace Trax.Core.Functional;

/// <summary>
/// Which side of an <see cref="Either{TLeft, TRight}"/> holds its value.
/// </summary>
public enum EitherStatus : byte
{
    /// <summary>Neither side: the value of <c>default(Either&lt;TLeft, TRight&gt;)</c>.</summary>
    IsBottom = 0,

    /// <summary>The left side, which by convention is the failure track.</summary>
    IsLeft = 1,

    /// <summary>The right side, which by convention is the success track.</summary>
    IsRight = 2,
}

/// <summary>
/// A value on one of two tracks: <c>Left</c>, which Trax uses for the exception that switched a train to the
/// failure track, or <c>Right</c>, the result it carried. A train's <c>Junctions()</c> returns
/// <c>Either&lt;Exception, TOutput&gt;</c>, and <c>RunEither</c> hands it to the caller.
/// </summary>
/// <typeparam name="TLeft">The left type; <see cref="Exception"/> in a train.</typeparam>
/// <typeparam name="TRight">The right type; the train's output.</typeparam>
/// <remarks>
/// Either side converts implicitly, so a junction chain can return a value or an exception where an
/// <c>Either</c> is expected. Neither side may be null. <c>default(Either&lt;TLeft, TRight&gt;)</c> is on
/// neither side (<see cref="EitherStatus.IsBottom"/>).
/// </remarks>
public readonly struct Either<TLeft, TRight> : IEquatable<Either<TLeft, TRight>>
{
    private readonly TLeft left;
    private readonly TRight right;

    private Either(EitherStatus state, TLeft left, TRight right)
    {
        State = state;
        this.left = left;
        this.right = right;
    }

    /// <summary>Which side holds the value.</summary>
    public EitherStatus State { get; }

    /// <summary>True when the value is on the left (failure) side.</summary>
    public bool IsLeft => State == EitherStatus.IsLeft;

    /// <summary>True when the value is on the right (success) side.</summary>
    public bool IsRight => State == EitherStatus.IsRight;

    /// <summary>True for <c>default(Either&lt;TLeft, TRight&gt;)</c>, which holds no value.</summary>
    public bool IsBottom => State == EitherStatus.IsBottom;

    /// <summary>An <c>Either</c> on the left side.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static Either<TLeft, TRight> Left(TLeft value) =>
        value is null
            ? throw new ArgumentNullException(
                nameof(value),
                "An Either cannot hold null on its left side."
            )
            : new(EitherStatus.IsLeft, value, default!);

    /// <summary>An <c>Either</c> on the right side.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static Either<TLeft, TRight> Right(TRight value) =>
        value is null
            ? throw new ArgumentNullException(
                nameof(value),
                "An Either cannot hold null on its right side."
            )
            : new(EitherStatus.IsRight, default!, value);

    /// <summary>Puts a value on the left side.</summary>
    public static implicit operator Either<TLeft, TRight>(TLeft value) => Left(value);

    /// <summary>Puts a value on the right side.</summary>
    public static implicit operator Either<TLeft, TRight>(TRight value) => Right(value);

    /// <summary>The right value.</summary>
    /// <exception cref="InvalidCastException">The value is not on the right side.</exception>
    public static explicit operator TRight(Either<TLeft, TRight> either) =>
        either.IsRight
            ? either.right
            : throw new InvalidCastException($"The Either is {either.State}, not IsRight.");

    /// <summary>The left value.</summary>
    /// <exception cref="InvalidCastException">The value is not on the left side.</exception>
    public static explicit operator TLeft(Either<TLeft, TRight> either) =>
        either.IsLeft
            ? either.left
            : throw new InvalidCastException($"The Either is {either.State}, not IsLeft.");

    /// <summary>
    /// Applies the function for whichever side holds the value, or <paramref name="Bottom"/> for
    /// <c>default(Either)</c>, which holds none.
    /// </summary>
    /// <exception cref="InvalidOperationException">The Either is on neither side and no <paramref name="Bottom"/> is given.</exception>
    public TResult Match<TResult>(
        Func<TRight, TResult> Right,
        Func<TLeft, TResult> Left,
        Func<TResult>? Bottom = null
    ) =>
        State switch
        {
            EitherStatus.IsRight => Right(right),
            EitherStatus.IsLeft => Left(left),
            _ => Bottom is null ? throw BottomException() : Bottom(),
        };

    /// <summary>
    /// Runs the action for whichever side holds the value, or <paramref name="Bottom"/> for
    /// <c>default(Either)</c>, which holds none.
    /// </summary>
    /// <exception cref="InvalidOperationException">The Either is on neither side and no <paramref name="Bottom"/> is given.</exception>
    public void Match(Action<TRight> Right, Action<TLeft> Left, Action? Bottom = null)
    {
        switch (State)
        {
            case EitherStatus.IsRight:
                Right(right);
                break;
            case EitherStatus.IsLeft:
                Left(left);
                break;
            default:
                if (Bottom is null)
                    throw BottomException();
                Bottom();
                break;
        }
    }

    /// <summary>Runs the action when the value is on the right side.</summary>
    public Unit IfRight(Action<TRight> action)
    {
        if (IsRight)
            action(right);
        return Unit.Default;
    }

    /// <summary>Runs the action when the value is on the left side.</summary>
    public Unit IfLeft(Action<TLeft> action)
    {
        if (IsLeft)
            action(left);
        return Unit.Default;
    }

    /// <summary>The same value on the other side: a right becomes a left, and a left a right.</summary>
    public Either<TRight, TLeft> Swap() =>
        State switch
        {
            EitherStatus.IsRight => new(EitherStatus.IsLeft, right, default!),
            EitherStatus.IsLeft => new(EitherStatus.IsRight, default!, left),
            _ => default,
        };

    /// <summary>
    /// The right value, or <c>default</c> when the value is not on the right side. Check
    /// <see cref="IsRight"/> first, or use <c>Swap().ValueUnsafe()</c> to read the left value.
    /// </summary>
    public TRight ValueUnsafe() => right;

    /// <inheritdoc />
    public bool Equals(Either<TLeft, TRight> other) =>
        State == other.State
        && State switch
        {
            EitherStatus.IsRight => EqualityComparer<TRight>.Default.Equals(right, other.right),
            EitherStatus.IsLeft => EqualityComparer<TLeft>.Default.Equals(left, other.left),
            _ => true,
        };

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Either<TLeft, TRight> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() =>
        State switch
        {
            EitherStatus.IsRight => HashCode.Combine(State, right),
            EitherStatus.IsLeft => HashCode.Combine(State, left),
            _ => 0,
        };

    /// <summary>Whether two values are on the same side and equal.</summary>
    public static bool operator ==(Either<TLeft, TRight> a, Either<TLeft, TRight> b) => a.Equals(b);

    /// <summary>Whether two values differ in side or value.</summary>
    public static bool operator !=(Either<TLeft, TRight> a, Either<TLeft, TRight> b) =>
        !a.Equals(b);

    /// <summary><c>Right(value)</c>, <c>Left(value)</c> or <c>Bottom</c>.</summary>
    public override string ToString() =>
        State switch
        {
            EitherStatus.IsRight => $"Right({right})",
            EitherStatus.IsLeft => $"Left({left})",
            _ => "Bottom",
        };

    private static InvalidOperationException BottomException() =>
        new("The Either holds no value: it is default(Either), on neither side.");
}
