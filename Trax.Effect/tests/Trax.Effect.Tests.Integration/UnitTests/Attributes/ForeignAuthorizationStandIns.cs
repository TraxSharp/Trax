namespace HotChocolate.Authorization;

/// <summary>
/// A stand-in for HotChocolate's attribute, in its real namespace with its real name, so the
/// full-name match under test is the one that runs in production. Trax.Effect does not reference
/// HotChocolate and must not start to, which is exactly why the ban matches on the name.
/// </summary>
[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
internal sealed class AuthorizeAttribute : Attribute;

[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
internal sealed class AllowAnonymousAttribute : Attribute;
