namespace Trax.Effect.Configuration.TraxBuilder;

/// <summary>
/// Marker service registered by <c>AddTrax()</c>. Used by <c>AddTraxDashboard()</c>
/// and <c>AddTraxGraphQL()</c> to verify that <c>AddTrax()</c> was called first.
/// </summary>
public sealed class TraxMarker;
