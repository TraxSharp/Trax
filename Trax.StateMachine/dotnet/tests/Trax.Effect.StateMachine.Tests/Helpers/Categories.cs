namespace Trax.Effect.StateMachine.Tests.Helpers;

/// <summary>The test categories CI selects on.</summary>
public static class Categories
{
    /// <summary>
    /// A test that runs or reads the TypeScript engine in <c>Trax.StateMachine/ts</c>, so a change on either side
    /// can break it. CI runs these in the conformance suite, which runs for a change anywhere in the folder; the C#
    /// suite leaves them out, so a change to the TypeScript engine alone does not rebuild the C# one.
    /// </summary>
    public const string CrossEngine = "CrossEngine";
}
