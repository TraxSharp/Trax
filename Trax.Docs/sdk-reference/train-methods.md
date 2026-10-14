---
title: Train Methods
description: "Index of the members of Train and ServiceTrain that declare, run and read a chain: Junctions, Chain, the decision steps, Run, RunEither and DeclaredChain."
type: landing
area: trains
parent: SDK Reference
has_children: true
nav_order: 3
---

# Train Methods

The members of `Train<TInput, TReturn>` (Trax.Core), inherited by `ServiceTrain<TIn, TOut>` (Trax.Effect), that declare a chain inside `Junctions()`, run a train, and read its chain without running it. The types themselves are under [Trains and Junctions](/docs/sdk-reference/trains-and-junctions).

## Declaring a chain

| Method | Description |
|--------|-------------|
| [Junctions](/docs/sdk-reference/train-methods/junctions) | The override that declares the train's chain |
| [Chain](/docs/sdk-reference/train-methods/chain) | Runs a junction, its input from Memory and its output stored back |
| [IChain](/docs/sdk-reference/train-methods/i-chain) | Runs a junction resolved by its interface from Memory or the container |
| [ShortCircuit](/docs/sdk-reference/train-methods/short-circuit) | Runs a junction whose success becomes the result `Resolve` returns; its failure is ignored |
| [Extract](/docs/sdk-reference/train-methods/extract) | Copies a member of a Memory value into its own Memory slot |
| [AddServices](/docs/sdk-reference/train-methods/add-services) | Stores services in Memory under their interfaces |
| [Resolve](/docs/sdk-reference/train-methods/resolve) | Ends the chain, taking the result out of Memory |

## Deciding and routing

| Method | Description |
|--------|-------------|
| [Decide](/docs/sdk-reference/train-methods/decide) | Asks a decider several typed questions about a value in Memory and stores the decisions |
| [Switch](/docs/sdk-reference/train-methods/switch) | Sends the train down one of several tracks, one per enum member |
| [Gate](/docs/sdk-reference/train-methods/gate) | Routes on the probability that a yes/no answer is yes |
| [Scale](/docs/sdk-reference/train-methods/scale) | Routes on where the state falls on an ordered scale |
| [Parallel](/docs/sdk-reference/train-methods/parallel) | Runs named branches side by side and joins them (experimental) |
| [Checkpoint](/docs/sdk-reference/train-methods/checkpoint) | Stores a declared state, so a later run of the same input resumes after it |

## Running a train

| Method | Description |
|--------|-------------|
| [Run](/docs/sdk-reference/train-methods/run) | Runs the train and throws its failure |
| [RunEither](/docs/sdk-reference/train-methods/run-either) | Runs the train and returns its failure as `Left` |

## Reading a chain

| Member | Description |
|--------|-------------|
| [DeclaredChain](/docs/sdk-reference/train-methods/declared-chain) | Reads the declared chain without running it; `ChainVerification.Verify` checks it |
| [ChainGraph](/docs/sdk-reference/train-methods/chain-graph) | The declared chain as a graph with stable node ids, canonical JSON and a hash |
