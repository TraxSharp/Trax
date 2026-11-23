namespace HotChocolate.Authorization;

[AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
internal sealed class AllowAnonymousAttribute : Attribute;
