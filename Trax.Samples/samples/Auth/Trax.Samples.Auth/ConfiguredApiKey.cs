namespace Trax.Samples.Auth;

/// <summary>A production API key as configuration holds it: never the key itself.</summary>
internal sealed record ConfiguredApiKey(string Id, string Salt, string Hash, string[] Roles);
