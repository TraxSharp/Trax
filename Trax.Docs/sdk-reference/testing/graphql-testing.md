---
title: Trax.Api.GraphQL.Testing
description: "Reference for Trax.Api.GraphQL.Testing: the cross-schema GraphQL guards, which check the edge manifest, batched loaders and declared parent reads."
type: reference
area: testing
package: Trax.Api.GraphQL.Testing
namespace: Trax.Api.GraphQL.Testing
source:
  - Trax.Api/src/Trax.Api.GraphQL.Testing/CrossSchemaGuards.cs
  - Trax.Api/src/Trax.Api.GraphQL.Testing/CrossSchemaGuardFixture.cs
parent: Testing
grand_parent: SDK Reference
nav_order: 4
---

# Trax.Api.GraphQL.Testing

The GraphQL guard package: checkers for the cross-schema GraphQL pattern, and an NUnit fixture that runs them. It depends on Trax.Core.Testing and Trax.Api.GraphQL.

## Signatures

```csharp fragment
namespace Trax.Api.GraphQL.Testing;

public static class CrossSchemaGuards
{
    public static GuardResult EdgeManifestIsValid(IReadOnlyList<CrossSchemaEdge> edges);
    public static GuardResult EdgeResolversUseLoader(ArchitectureGuardOptions options, string crossSchemaProjectSuffix = ".CrossSchema");
    public static GuardResult ExtensionResolversDeclareParentRequirements(ArchitectureGuardOptions options);
}

[TestFixture]
public abstract class CrossSchemaGuardFixture
{
    protected virtual ArchitectureGuardOptions Options { get; }
    protected virtual IReadOnlyList<CrossSchemaEdge> Edges { get; }
    protected virtual bool ExpectsCrossSchemaResolvers { get; }
    protected virtual bool ExpectsParentResolvers { get; }

    [Test] public void Cross_schema_edge_manifest_is_valid();
    [Test] public void Cross_schema_edge_resolvers_use_the_batched_loader();
    [Test] public void Extension_resolvers_declare_what_they_read_off_their_parent();

    protected static void AssertInspected(GuardResult result, bool expected, string what, string optOut, ArchitectureGuardOptions options);
}
```

## Members

### CrossSchemaGuards

| Checker | Checks |
|---------|--------|
| `EdgeManifestIsValid(edges)` | Each [CrossSchemaEdge](/docs/sdk-reference/graphql-api/cross-schema-data-loaders) names a real integer foreign key on its source, a target owned as a `DbSet` by the declared target context, and a camelCase field name |
| `EdgeResolversUseLoader(options, crossSchemaProjectSuffix)` | Every resolver on a type extension in a project whose name ends in `crossSchemaProjectSuffix` routes through a `CrossSchemaLoader<,>`: a public method of an `[ExtendObjectType]` class, or of a source-generated `[ObjectType<T>]` extension, that is not `[GraphQLIgnore]` and names `CrossSchemaLoader<,>` in its parameters or body |
| `ExtensionResolversDeclareParentRequirements(options)` | Every property a `[Parent]` resolver reads off its parent, other than the key Trax adds itself, is declared in `[Parent(requires: ...)]` |

### CrossSchemaGuardFixture

| Member | Default | Description |
|--------|---------|-------------|
| `Options` | `new()` | Scan roots and allowlists |
| `Edges` | empty | The edge manifest; empty passes the manifest check |
| `ExpectsCrossSchemaResolvers` | `true` | Whether the scan roots must hold at least one cross-schema `[ExtendObjectType]` resolver |
| `ExpectsParentResolvers` | `true` | Whether they must hold at least one `[Parent]` resolver |
| `Cross_schema_edge_manifest_is_valid` | | Runs `EdgeManifestIsValid(Edges)`; fails on any offender |
| `Cross_schema_edge_resolvers_use_the_batched_loader` | | Runs `EdgeResolversUseLoader(Options)`; fails when it inspected nothing and `ExpectsCrossSchemaResolvers` is true, then on any offender |
| `Extension_resolvers_declare_what_they_read_off_their_parent` | | Runs `ExtensionResolversDeclareParentRequirements(Options)`; fails when it inspected nothing and `ExpectsParentResolvers` is true, then on any offender |
| `AssertInspected(result, expected, what, optOut, options)` | | The shared "inspected nothing" failure, naming the scan roots and the property that opts out. Protected, for a subclass's own guards. |

## Examples

```csharp compile
using Trax.Api.GraphQL.Testing;
using Trax.Core.Testing;

var result = CrossSchemaGuards.ExtensionResolversDeclareParentRequirements(
    new ArchitectureGuardOptions { SourceScanRoots = ["src"] }
);

if (result.Inspected == 0 || !result.Passed)
    throw new InvalidOperationException(result.FailureMessage);
```

The fixture, subclassed in an NUnit test project:

```csharp fragment
[TestFixture]
public sealed class CrossSchemaGuards : CrossSchemaGuardFixture
{
    protected override ArchitectureGuardOptions Options => new() { SourceScanRoots = ["libs"] };
    protected override IReadOnlyList<CrossSchemaEdge> Edges => MyCrossSchemaEdges.All;
}
```

## See also

- [Cross-schema data loaders](/docs/sdk-reference/graphql-api/cross-schema-data-loaders)
- [Architecture Guards](/docs/reference/architecture-guards)
