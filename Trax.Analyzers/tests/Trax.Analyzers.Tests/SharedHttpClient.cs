namespace Trax.Analyzers.Tests.Harness;

/// <summary>
/// The client the copied harness (Harness/NuGetPackages.cs) downloads a test's reference package
/// with when the restore has not already put it in the NuGet cache. Upstream took it from its
/// build SDK; this is Trax's own. Where the test step has no network (trax-test's runner), such a
/// download fails and so does the test: pin the package instead.
/// </summary>
internal static class SharedHttpClient
{
    public static HttpClient InstanceWithAutoRedirect { get; } =
        new(new HttpClientHandler { AllowAutoRedirect = true });
}
