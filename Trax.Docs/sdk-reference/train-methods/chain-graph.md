---
layout: default
title: ChainGraph
description: "Reference for ChainGraph: a train's declared chain as a graph with stable node ids, canonical JSON and a hash, and the id of the running step."
parent: Train Methods
grand_parent: SDK Reference
nav_order: 14
---

# ChainGraph

A train's declared chain as an immutable graph: every step it declares, the types that flow
through each one, every track a routing step declares, and an id for each node. It is built from
[DeclaredChain](/docs/sdk-reference/train-methods/declared-chain), so nothing runs to draw it.
It is the shape a tool draws, diffs or checks in a test; `ChainRecorder` stays the input to
`ChainVerification.Verify`.

## Signatures

```csharp
namespace Trax.Core.Monad;

public sealed record ChainGraph(
    string Train,
    string Input,
    string Output,
    IReadOnlyList<ChainGraphNode> Nodes,
    IReadOnlyList<string> Refusals
)
{
    public static ChainGraph From(ChainRecorder chain, Type train, Type input, Type output);
    public static string? CurrentNodeId { get; }
    public string Hash { get; }
    public string ToJson();
}

public sealed record ChainGraphNode(
    string Id,
    ChainStepKind Kind,
    string? Junction,
    string? In,
    string? Out,
    bool Opaque,
    IReadOnlyList<ChainGraphTrack> Tracks
);

public sealed record ChainGraphTrack(
    string Name,
    string? Description,
    bool IsFallback,
    IReadOnlyList<ChainGraphNode> Nodes
);
```

| `ChainGraph` member | Description |
|---|---|
| `From(chain, train, input, output)` | Builds the graph of a recording from `DeclaredChain()`. `train`, `input` and `output` are the train type and its `TInput` and `TReturn` |
| `Train` | The train's full type name |
| `Input`, `Output` | The train's input and return types, by their readable names (`List<Order>`, not the CLR's backtick form) |
| `Nodes` | The chain's steps, in the order it declares them |
| `Refusals` | What the declaration did that no step can express, as `ChainRecorder.Refusals` lists it |
| `ToJson()` | The graph as canonical JSON. See [Canonical JSON and Hash](#canonical-json-and-hash) |
| `Hash` | SHA-256 of `ToJson()`, as 64 lowercase hex digits |
| `CurrentNodeId` | The id of the node a run is executing on this async flow, or null. See [CurrentNodeId](#currentnodeid) |

| `ChainGraphNode` member | Description |
|---|---|
| `Id` | The node's id, unique within its graph. See [Node ids](#node-ids) |
| `Kind` | Which chain primitive declared the step (`ChainStepKind`) |
| `Junction` | The junction type, the decider (`IDecider`) for a `Decide` step, or null for a step that names neither |
| `In`, `Out` | The types the step consumes from and contributes to Memory, or null |
| `Opaque` | True when what runs is decided only at run time. See [Opaque nodes](#opaque-nodes) |
| `Tracks` | The tracks of a `Switch`, `Gate` or `Scale` step, in declared order; empty for any other step |

| `ChainGraphTrack` member | Description |
|---|---|
| `Name` | An enum member's name, or `Yes`, `No`, `Unsure` or `Otherwise` |
| `Description` | What the track is for, as offered to the decider, or null |
| `IsFallback` | True for the `Otherwise` or `Unsure` track |
| `Nodes` | The track's own steps, in declared order |

## Node ids

An id names the step, not its position. It is the step's key, then `#` and how many earlier
steps in the same chain or track share that key:

| Step | Key | Example id |
|---|---|---|
| `Chain`, `IChain`, `ShortCircuit` | The junction type (the interface, for `IChain`) | `SearchWeb#0` |
| `Decide` | The decision it puts in Memory | `Decide<ChoiceDecision<Source>>#0` |
| `Switch`, `Gate`, `Scale` | The kind and the type it routes on | `Switch<Source>#0` |
| `Extract` | Both types | `Extract<Int32, Int64>#0` |
| `Seed` | The seeded type | `Seed<IDecider>#0` |
| `Resolve` | `Resolve` | `Resolve#0` |

A node inside a track is prefixed with its routing step's id and the track's name, and numbered
within that track: `Switch<Source>#0/Papers/SearchPapers#0`.

Naming the step is what keeps an id stable while a train changes. Insert a `Chain<Audit>()` at
the head of a chain and every other node keeps its id, where a positional id would shift every
step after it, and with it everything keyed on those ids: a recorded run, a drawing's layout, a
review comment on one step. Only a second step with the same key takes a new ordinal, and only
the occurrences after it move.

## Opaque nodes

An `IChain<TJunction>()` step names an interface, and the junction behind it is whatever Memory
or the container holds when the run gets there. The graph cannot say what that step does, so its
node is marked `Opaque`, with the interface as its `Junction`. Every other node names the type
that runs.

## Canonical JSON and Hash

`ToJson()` writes the graph with its properties in a fixed order, no insignificant whitespace, and
absent values written as `null` rather than left out, so the same graph always gives the same
text. Generic names are written as they read (`<`, `>` are not escaped). `Hash` is computed over
that text, so two reads of an unchanged train give the same hash, and any change to a step, a type
or a track gives another. Compare hashes to tell whether a train's shape changed; compare the JSON
to see how.

```json
{"id":"Switch<Source>#0","kind":"Switch","junction":null,"in":"ChoiceDecision<Source>","out":"TrackTaken<Source>","opaque":false,"tracks":[{"name":"Web","description":null,"isFallback":false,"nodes":[{"id":"Switch<Source>#0/Web/SearchWeb#0", ...}]}, ...]}
```

## CurrentNodeId

While a run executes a junction (`Chain`, `IChain` or `ShortCircuit`), asks a question
(`Decide`) or routes (`Switch`, `Gate`, `Scale`), `CurrentNodeId` holds the id the graph drew for
that step. It is set for the duration of the step on the step's async flow, so anything the step
calls can say which node it belongs to: the junction itself, the junction effects around an
`EffectJunction`, or an `IDecisionObserver` hearing a decision or a routing. Outside a step it is
null, and `Extract`, `Seed` and `Resolve` set nothing. A train run inside a junction sets its own
ids for its own steps, and the outer id returns when the inner run does.

The run numbers its steps the same way the graph does, so a running step's id is always the id of
a node in that train's graph.

## Reading it from a host

A host serves the graph of each registered train through `ITrainChainGraphs.Find(name)`, by its
canonical name, reading each chain once and keeping it. The operations API returns it as
[`declaredChain`](/docs/sdk-reference/graphql-api/queries#declaredchain), and draws one run on it as
[`runGraph`](/docs/sdk-reference/graphql-api/queries#rungraph), matching each recorded step to its
node by the `nodeId` junction events record; the dashboard's run page shows the same
[run graph](/docs/dashboard).

`ITrainChainGraphs.FindDeclared(name)` looks a train up the same way and
returns the `ChainRecorder` the graph was drawn from, with the train's class and its input and output
types, as a `DeclaredTrainChain`. It is what `IRunResumes.Check` needs to decide whether a failed run
can resume from a checkpoint; the scheduler's retries and the operator's resume ask through it.

## Example

A test that fails when a train's shape changes, by comparing its graph to a committed copy:

```csharp
[Test]
public void ResearchTopicTrain_KeepsItsShape()
{
    var chain = new ResearchTopicTrain().DeclaredChain();
    var graph = ChainGraph.From(chain, typeof(ResearchTopicTrain), typeof(ResearchInput), typeof(ResearchReport));

    graph.Refusals.Should().BeEmpty();
    graph.ToJson().Should().Be(File.ReadAllText("ResearchTopicTrain.chain.json").TrimEnd());
}
```

## Package

```
dotnet add package Trax.Core
```
