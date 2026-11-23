namespace Trax.Analyzers.Internals;

// The attribute can be defined in multiple assemblies, so it's identified by its full name only
internal static class AnnotationAttributes
{
    public static bool IsCultureSensitiveAttributeSymbol(ITypeSymbol? symbol)
    {
        // Trax.Analyzers.Annotations.CultureInsensitiveTypeAttribute        
        return symbol is INamedTypeSymbol
        {
            Name: "CultureInsensitiveTypeAttribute",
            ContainingSymbol: INamespaceSymbol
            {
                Name: "Annotations",
                ContainingSymbol: INamespaceSymbol
                {
                    Name: "Analyzers",
                    ContainingSymbol: INamespaceSymbol
                    {
                        Name: "Trax",
                        ContainingSymbol: INamespaceSymbol { IsGlobalNamespace: true }
                    }
                }
            }
        };
    }

    public static bool IsCultureInsensitiveAttributeSymbol(ITypeSymbol? symbol)
    {
        // Trax.Analyzers.Annotations.CultureInsensitiveAttribute
        return symbol is INamedTypeSymbol
        {
            Name: "CultureInsensitiveAttribute",
            ContainingSymbol: INamespaceSymbol
            {
                Name: "Annotations",
                ContainingSymbol: INamespaceSymbol
                {
                    Name: "Analyzers",
                    ContainingSymbol: INamespaceSymbol
                    {
                        Name: "Trax",
                        ContainingSymbol: INamespaceSymbol { IsGlobalNamespace: true }
                    }
                }
            }
        };
    }

    public static bool IsRequireNamedArgumentAttributeSymbol(ITypeSymbol? symbol)
    {
        // Trax.Analyzers.Annotations.RequireNamedArgumentAttribute        
        return symbol is INamedTypeSymbol
        {
            Name: "RequireNamedArgumentAttribute",
            ContainingSymbol: INamespaceSymbol
            {
                Name: "Annotations",
                ContainingSymbol: INamespaceSymbol
                {
                    Name: "Analyzers",
                    ContainingSymbol: INamespaceSymbol
                    {
                        Name: "Trax",
                        ContainingSymbol: INamespaceSymbol { IsGlobalNamespace: true }
                    }
                }
            }
        };
    }

    public static bool IsExcludeFromBlockingCallAnalysisAttributeSymbol(ITypeSymbol? symbol)
    {
        // Trax.Analyzers.Annotations.ExcludeFromBlockingCallAnalysisAttribute        
        return symbol is INamedTypeSymbol
        {
            Name: "ExcludeFromBlockingCallAnalysisAttribute",
            ContainingSymbol: INamespaceSymbol
            {
                Name: "Annotations",
                ContainingSymbol: INamespaceSymbol
                {
                    Name: "Analyzers",
                    ContainingSymbol: INamespaceSymbol
                    {
                        Name: "Trax",
                        ContainingSymbol: INamespaceSymbol { IsGlobalNamespace: true }
                    }
                }
            }
        };
    }

    public static bool IsExcludeFromCancellationTokenAnalysisAttributeSymbol(ITypeSymbol? symbol)
    {
        // Trax.Analyzers.Annotations.ExcludeFromCancellationTokenAnalysisAttribute
        return symbol is INamedTypeSymbol
        {
            Name: "ExcludeFromCancellationTokenAnalysisAttribute",
            ContainingSymbol: INamespaceSymbol
            {
                Name: "Annotations",
                ContainingSymbol: INamespaceSymbol
                {
                    Name: "Analyzers",
                    ContainingSymbol: INamespaceSymbol
                    {
                        Name: "Trax",
                        ContainingSymbol: INamespaceSymbol { IsGlobalNamespace: true }
                    }
                }
            }
        };
    }

    public static bool IsNonAwaitableTypeAttributeSymbol(ITypeSymbol? symbol)
    {
        // Trax.Analyzers.Annotations.NonAwaitableTypeAttribute
        return symbol is INamedTypeSymbol
        {
            Name: "NonAwaitableTypeAttribute",
            ContainingSymbol: INamespaceSymbol
            {
                Name: "Annotations",
                ContainingSymbol: INamespaceSymbol
                {
                    Name: "Analyzers",
                    ContainingSymbol: INamespaceSymbol
                    {
                        Name: "Trax",
                        ContainingSymbol: INamespaceSymbol { IsGlobalNamespace: true }
                    }
                }
            }
        };
    }

    public static bool IsNonAsyncDisposableTypeAttributeSymbol(ITypeSymbol? symbol)
    {
        // Trax.Analyzers.Annotations.NonAsyncDisposableTypeAttribute
        return symbol is INamedTypeSymbol
        {
            Name: "NonAsyncDisposableTypeAttribute",
            ContainingSymbol: INamespaceSymbol
            {
                Name: "Annotations",
                ContainingSymbol: INamespaceSymbol
                {
                    Name: "Analyzers",
                    ContainingSymbol: INamespaceSymbol
                    {
                        Name: "Trax",
                        ContainingSymbol: INamespaceSymbol { IsGlobalNamespace: true }
                    }
                }
            }
        };
    }

    public static bool IsDoNotIgnoreAttributeSymbol(ITypeSymbol? symbol)
    {
        // Trax.Analyzers.Annotations.DoNotIgnoreAttribute
        return symbol is INamedTypeSymbol
        {
            Name: "DoNotIgnoreAttribute",
            ContainingSymbol: INamespaceSymbol
            {
                Name: "Annotations",
                ContainingSymbol: INamespaceSymbol
                {
                    Name: "Analyzers",
                    ContainingSymbol: INamespaceSymbol
                    {
                        Name: "Trax",
                        ContainingSymbol: INamespaceSymbol { IsGlobalNamespace: true }
                    }
                }
            }
        };
    }

    public static bool IsStructuredLogFieldAttributeSymbol(ITypeSymbol? symbol)
    {
        // Trax.Analyzers.Annotations.StructuredLogFieldAttribute
        return symbol is INamedTypeSymbol
        {
            Name: "StructuredLogFieldAttribute",
            ContainingSymbol: INamespaceSymbol
            {
                Name: "Annotations",
                ContainingSymbol: INamespaceSymbol
                {
                    Name: "Analyzers",
                    ContainingSymbol: INamespaceSymbol
                    {
                        Name: "Trax",
                        ContainingSymbol: INamespaceSymbol { IsGlobalNamespace: true }
                    }
                }
            }
        };
    }
}
