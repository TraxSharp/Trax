---
title: Dashboard component model types
description: "Reference for ServerDataResult, DagLayout, PositionedNode and PositionedEdge, the types the dashboard's grid and graph components expose."
type: reference
area: dashboard
package: Trax.Dashboard
namespace:
  - Trax.Dashboard.Models
  - Trax.Dashboard.Utilities
parent: Dashboard API
grand_parent: SDK Reference
nav_order: 5
source:
  - Trax.Dashboard/src/Trax.Dashboard/Models/ServerDataResult.cs
  - Trax.Dashboard/src/Trax.Dashboard/Utilities/DagLayoutEngine.cs
---

# Dashboard component model types

Types that are public only because a parameter of one of the dashboard's own components exposes
them: `ServerDataResult<T>` through `TraxDataGrid.ServerLoadData`, and `DagLayout` through
`DagGraph.Layout`. They are not meant for use outside the package, and `ServerDataResult<T>` is
hidden from IntelliSense.

## Signatures

```csharp fragment
public record ServerDataResult<T>(IEnumerable<T> Items, int TotalCount);

public class DagLayout
{
    public IReadOnlyList<PositionedNode> Nodes { get; init; }
    public IReadOnlyList<PositionedEdge> Edges { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}

public class PositionedNode
{
    public long Id { get; init; }
    public string Label { get; init; }
    public bool IsHighlighted { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}

public class PositionedEdge
{
    public long FromId { get; init; }
    public long ToId { get; init; }
    public string PathData { get; init; }
}
```

## Properties

### ServerDataResult

| Property | Type | Description |
|---|---|---|
| `Items` | `IEnumerable<T>` | The rows of the requested page, after filtering, sorting, skip and take |
| `TotalCount` | `int` | The number of rows matching the filter before paging |

### DagLayout

| Property | Type | Description |
|---|---|---|
| `Nodes` | `IReadOnlyList<PositionedNode>` | Every input node, positioned |
| `Edges` | `IReadOnlyList<PositionedEdge>` | The input edges whose two ends are both nodes of the graph |
| `Width`, `Height` | `double` | The size of the drawing, in SVG user units |

### PositionedNode

| Property | Type | Description |
|---|---|---|
| `Id`, `Label`, `IsHighlighted` | `long`, `string`, `bool` | From the node that was laid out |
| `X`, `Y` | `double` | Left and top edge. Nodes in one layer share `X`; layers run left to right |
| `Width`, `Height` | `double` | Always 180 and 40 |

### PositionedEdge

| Property | Type | Description |
|---|---|---|
| `FromId`, `ToId` | `long` | The upstream and downstream node ids |
| `PathData` | `string` | SVG cubic bezier path data (`M x1,y1 C cx1,cy1 cx2,cy2 x2,y2`) |

## See also

- [Dashboard](/docs/dashboard)
