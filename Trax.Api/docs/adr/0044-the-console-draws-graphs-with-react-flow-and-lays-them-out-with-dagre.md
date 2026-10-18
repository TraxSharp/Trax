---
authors: [Theauxm]
areas: [platform]
status: accepted
---

# The console draws graphs with React Flow and lays them out with dagre

The console draws a run's chain, a train's chain, a machine's states and transitions, and manifest-group
dependencies. Until 2026-10-18 it drew only the last, with a hand-written layered layout ported from the Blazor
dashboard that refuses a graph with a cycle, and every machine with a loop has one. The console renders graphs with
`@xyflow/react` (React Flow) and lays them out with `@dagrejs/dagre`, through one `Graph` component every page uses.
Both are MIT,
and what they bring in is MIT or ISC (d3's zoom and drag, beside the d3 modules recharts already ships).

## Status

**Accepted.**

## Considered options

**ELK (`elkjs`).** The better layout: ports for labelled edges, edge routing around nodes, tidy cycles. Rejected
for its licence. It is EPL-2.0, a copyleft licence, and the console bundles its dependencies into a package every
host serving the console carries; what Trax ships stays under MIT and permissive licences only.

**Extend the hand-written layout.** No dependency, but cycle breaking, edge routing, pan, zoom and keyboard
selection are each real work, and the result would be a layout engine nobody else maintains.

**Mermaid**, which the CLI already emits for `trax machine show`. Static output: no selection to sync with a
timeline, no per-node counts or badges that update live. It also injects generated styles, which the console's strict
CSP would have to allow.

## Consequences

dagre breaks a cycle by reversing an edge for layout and routes edges as polylines that can cross other nodes. The
`Graph` component draws a reversed edge in its true direction and as a curve, so a machine's loop reads as a loop.
The console's build refuses a bundle the CSP would block (`scripts/check-bundle.mjs`).

## Exemplars

**Enforced elsewhere:** `scripts/check-bundle.mjs` in `Trax.Api.Dashboard/ClientApp`, run by `npm run build`, fails
a bundle that needs `eval` or inline script the console's CSP refuses.

Not covered: nothing stops a page drawing its own SVG instead of using the shared `Graph` component, and nothing
checks a new dependency's licence.

## Changelog

- **2026-10-18**: Recorded.
