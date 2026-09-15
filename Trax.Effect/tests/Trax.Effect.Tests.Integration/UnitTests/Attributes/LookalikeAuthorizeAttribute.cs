namespace Lookalike;

/// <summary>
/// Same short name as HotChocolate's, different namespace. The ban matches on the full name, so
/// this must not trip it.
/// </summary>
[AttributeUsage(AttributeTargets.All)]
internal sealed class AuthorizeAttribute : Attribute;
