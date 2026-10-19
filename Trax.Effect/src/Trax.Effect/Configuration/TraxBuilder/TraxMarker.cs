using System.ComponentModel;

namespace Trax.Effect.Configuration.TraxBuilder;

/// <summary>
/// Marker service registered by <c>AddTrax()</c>. Used by <c>AddTraxGraphQL()</c> to
/// verify that <c>AddTrax()</c> was called first.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/configuration/add-trax">SDK reference</seealso>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class TraxMarker;
