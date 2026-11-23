namespace Trax.Analyzers.Internals;

internal record struct OverloadParameterType(ITypeSymbol? Symbol, bool AllowInherits = false);
