namespace Trax.Testing;

/// <summary>
/// A null where a signature says there cannot be one, for a test of what the code does when a
/// caller ignores the annotation. The null-forgiving operator is banned (the TraxBanNullForgiving
/// group of the Trax.Analyzers profile), and a test that passes null on purpose says so by name.
/// </summary>
internal static class Null
{
    // An array of a reference type starts out holding nulls, whatever the element type claims.
    public static T Of<T>()
        where T : class => (new T[1])[0];
}
