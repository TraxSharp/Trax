namespace Trax.Core.Functional;

/// <summary>
/// "No meaningful value": what a junction or train returns when it does its work but carries nothing new.
/// It stands in for <c>void</c> where C# needs a type, as in <c>Junction&lt;TInput, Unit&gt;</c> or
/// <c>Task&lt;Unit&gt;</c>. It has one value, <see cref="Default"/>, and every <c>Unit</c> equals every other.
/// </summary>
public readonly struct Unit : IEquatable<Unit>, IComparable<Unit>
{
    /// <summary>The only value of <see cref="Unit"/>.</summary>
    public static readonly Unit Default = default;

    /// <inheritdoc />
    public bool Equals(Unit other) => true;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Unit;

    /// <inheritdoc />
    public override int GetHashCode() => 0;

    /// <inheritdoc />
    public int CompareTo(Unit other) => 0;

    /// <summary>Always true.</summary>
    public static bool operator ==(Unit a, Unit b) => true;

    /// <summary>Always false.</summary>
    public static bool operator !=(Unit a, Unit b) => false;

    /// <summary><c>()</c>.</summary>
    public override string ToString() => "()";
}
