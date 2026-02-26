using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Trax.Core.Analyzers.Analysis;

/// <summary>
/// Simulates the runtime Memory dictionary as a set of type symbols.
/// Tracks which types are available at each point in the workflow chain.
///
/// Matches runtime behavior:
/// - Tuple outputs are decomposed into individual component types (not stored as tuples)
/// - Concrete types are stored alongside all their implemented interfaces
/// </summary>
internal sealed class MemorySimulator
{
    private const string UnitMetadataName = "LanguageExt.Unit";

    private readonly HashSet<ITypeSymbol> _types;

    public MemorySimulator()
    {
        _types = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
    }

    /// <summary>
    /// Seeds Memory with Unit and the workflow input type.
    /// Routes through AddType so tuple inputs get decomposed and interfaces are stored.
    /// </summary>
    public void Initialize(ITypeSymbol inputType, INamedTypeSymbol? unitType)
    {
        if (unitType != null)
            _types.Add(unitType);

        // Activate decomposes tuples and stores interfaces — route through AddType
        AddType(inputType);
    }

    /// <summary>
    /// Adds a type to Memory. Mirrors the runtime behavior:
    /// - ValueTuples are decomposed into individual component types
    /// - Concrete types are stored alongside all their implemented interfaces
    /// - Unit is skipped (always present)
    /// </summary>
    public void AddType(ITypeSymbol type)
    {
        if (IsUnit(type))
            return;

        // Tuple outputs: decompose into components instead of storing the tuple itself
        if (type is INamedTypeSymbol named && named.IsTupleType)
        {
            AddTupleComponents(named);
            return;
        }

        // Store the concrete type
        _types.Add(type);

        // Store all interfaces the type implements (mirrors runtime Activate/AddTupleToMemory)
        foreach (var iface in type.AllInterfaces)
        {
            _types.Add(iface);
        }
    }

    /// <summary>
    /// Checks whether a type is currently available in Memory.
    /// </summary>
    public bool Contains(ITypeSymbol type)
    {
        return _types.Contains(type);
    }

    /// <summary>
    /// Checks whether all component types of a ValueTuple are in Memory.
    /// Returns the list of missing component type symbols, or empty if all present.
    /// </summary>
    public List<ITypeSymbol> GetMissingTupleComponents(INamedTypeSymbol tupleType)
    {
        var missing = new List<ITypeSymbol>();

        foreach (var element in tupleType.TupleElements)
        {
            if (!_types.Contains(element.Type))
            {
                missing.Add(element.Type);
            }
        }

        return missing;
    }

    /// <summary>
    /// Returns a comma-separated string of all available type names, for diagnostic messages.
    /// </summary>
    public string GetAvailableTypesString()
    {
        return string.Join(
            ", ",
            _types.Select(t => t.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat))
        );
    }

    /// <summary>
    /// Decomposes a ValueTuple into its component types, adding each component
    /// and its interfaces to Memory. Does NOT recursively decompose nested tuples.
    /// Matches runtime AddTupleToMemory behavior.
    /// </summary>
    private void AddTupleComponents(INamedTypeSymbol tupleType)
    {
        foreach (var element in tupleType.TupleElements)
        {
            var elementType = element.Type;

            if (IsUnit(elementType))
                continue;

            // Add the component's concrete type
            _types.Add(elementType);

            // Add all interfaces of the component type
            foreach (var iface in elementType.AllInterfaces)
            {
                _types.Add(iface);
            }
        }
    }

    private static bool IsUnit(ITypeSymbol type)
    {
        return type.ToDisplayString() == UnitMetadataName;
    }
}
