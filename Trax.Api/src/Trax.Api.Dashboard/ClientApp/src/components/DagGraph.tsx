import { useId } from "react";
import type { DagLayout } from "../lib/dagLayout";

// SVG renderer for a computed DAG layout. Mirrors the Blazor DagGraph: rounded-rect nodes, cubic
// bezier edges with arrowheads, a highlighted focal node, and click-through per node.
export function DagGraph({
  layout,
  onNodeClick,
}: {
  layout: DagLayout;
  onNodeClick?: (id: number) => void;
}) {
  const markerId = useId();

  if (layout.nodes.length === 0) {
    return (
      <p className="text-sm text-muted text-center py-8">
        No dependency graph to display.
      </p>
    );
  }

  return (
    <div className="overflow-auto">
      <svg
        xmlns="http://www.w3.org/2000/svg"
        width={layout.width}
        height={layout.height}
        viewBox={`0 0 ${layout.width} ${layout.height}`}
        className="max-w-none"
        role="img"
        aria-label="Dependency graph"
      >
        <defs>
          <marker
            id={markerId}
            markerWidth={10}
            markerHeight={7}
            refX={10}
            refY={3.5}
            orient="auto"
          >
            <polygon points="0 0, 10 3.5, 0 7" className="fill-muted" />
          </marker>
        </defs>

        {layout.edges.map((e) => (
          <path
            key={`${e.fromId}-${e.toId}`}
            d={e.pathData}
            fill="none"
            className="stroke-line-strong"
            strokeWidth={1.5}
            markerEnd={`url(#${markerId})`}
          />
        ))}

        {layout.nodes.map((n) => {
          const long = n.label.length > 22;
          return (
            <g
              key={n.id}
              className={onNodeClick ? "cursor-pointer" : undefined}
              onClick={onNodeClick ? () => onNodeClick(n.id) : undefined}
            >
              <rect
                x={n.x}
                y={n.y}
                width={n.width}
                height={n.height}
                rx={8}
                ry={8}
                className={
                  n.isHighlighted
                    ? "fill-accent-soft stroke-accent"
                    : "fill-surface stroke-line-strong hover:stroke-accent"
                }
                strokeWidth={n.isHighlighted ? 2 : 1}
              />
              <text
                x={n.x + n.width / 2}
                y={n.y + n.height / 2}
                dominantBaseline="central"
                textAnchor="middle"
                textLength={long ? n.width - 20 : undefined}
                lengthAdjust={long ? "spacingAndGlyphs" : undefined}
                className={
                  n.isHighlighted
                    ? "fill-accent-fg text-[13px] font-medium"
                    : "fill-fg text-[13px]"
                }
              >
                <title>{n.label}</title>
                {n.label}
              </text>
            </g>
          );
        })}
      </svg>
    </div>
  );
}
