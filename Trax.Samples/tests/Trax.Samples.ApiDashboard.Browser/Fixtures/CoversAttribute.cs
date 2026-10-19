namespace Trax.Samples.ApiDashboard.Browser.Fixtures;

/// <summary>
/// What a browser test really drives in the console: a route it loads and reads
/// (<c>route:/runs/:id</c>, as <c>ClientApp/src/AppRoutes.tsx</c> spells it), a mutation it sends from the
/// control that sends it and checks on the host (<c>mutation:RequeueDeadLetter</c>), or a subscription whose
/// push it watches reach the page (<c>subscription:OnJunctionEvent</c>). Repeat it for each.
/// <c>Tests/ConsoleCoverageTests</c> fails on a route or operation no test names, and on a name that no longer
/// exists.
/// </summary>
/// <remarks>
/// Name only what the test asserts on. A page the test passes through on its way somewhere else is not covered
/// by it.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class CoversAttribute(string what) : Attribute
{
    public string What { get; } = what;
}
