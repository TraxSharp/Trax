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
