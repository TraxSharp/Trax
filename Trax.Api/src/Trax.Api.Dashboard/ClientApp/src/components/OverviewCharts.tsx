import type { ReactNode } from "react";
import { Link, useHref, useNavigate } from "react-router-dom";
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Legend,
  Line,
  LineChart,
  Rectangle,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";

// The chart colours are theme tokens (src/index.css), so the inline SVG recharts draws follows the
// light/dark switch without re-rendering.
const COMPLETED = "var(--chart-completed)";
const FAILED = "var(--chart-failed)";
const CANCELLED = "var(--chart-cancelled)";
// Distinct hues for the throughput series (top-3 trains + "Other").
const SERIES_COLORS = [
  "var(--chart-series-1)",
  "var(--chart-series-2)",
  "var(--chart-series-3)",
  "var(--chart-series-4)",
];
const AXIS = "var(--chart-axis)";
const GRID = "var(--chart-grid)";
const CURSOR = { fill: "var(--chart-cursor)" };

interface Bucket {
  timestamp: string;
  completed: number;
  failed: number;
  cancelled: number;
}

type RunOutcome = "COMPLETED" | "FAILED" | "CANCELLED";

// Stacked bar chart of run outcomes over the selected window. With linkFor, each segment of a bar
// is a link to the runs it counts.
export function ExecutionsOverTimeChart({
  buckets,
  linkFor,
}: {
  buckets: Bucket[];
  linkFor?: (timestamp: string, state: RunOutcome) => string;
}) {
  const data = buckets.map((b) => ({
    ...b,
    label: new Date(b.timestamp).toLocaleTimeString([], {
      hour: "2-digit",
      minute: "2-digit",
    }),
  }));

  if (data.every((d) => d.completed + d.failed + d.cancelled === 0))
    return <Empty>No runs in this window.</Empty>;

  const segment = (state: RunOutcome) =>
    function Segment(props: BarShapeProps) {
      const bucket = props.payload as Bucket & { label: string };
      const count = bucket[state.toLowerCase() as "completed" | "failed" | "cancelled"];
      const bar = <Rectangle {...props} />;
      if (!linkFor || !count) return bar;
      return (
        <ChartLink to={linkFor(bucket.timestamp, state)} label={`${count} ${state.toLowerCase()} at ${bucket.label}`}>
          {bar}
        </ChartLink>
      );
    };

  return (
    <ResponsiveContainer width="100%" height={220}>
      <BarChart data={data} margin={{ top: 4, right: 8, left: -16, bottom: 0 }}>
        <CartesianGrid strokeDasharray="3 3" stroke={GRID} vertical={false} />
        <XAxis
          dataKey="label"
          tick={{ fill: AXIS, fontSize: 11 }}
          interval="preserveStartEnd"
          minTickGap={24}
        />
        <YAxis tick={{ fill: AXIS, fontSize: 11 }} allowDecimals={false} />
        <Tooltip
          contentStyle={TOOLTIP}
          cursor={CURSOR}
        />
        <Bar dataKey="completed" stackId="a" fill={COMPLETED} name="Completed" shape={segment("COMPLETED")} />
        <Bar dataKey="failed" stackId="a" fill={FAILED} name="Failed" shape={segment("FAILED")} />
        <Bar dataKey="cancelled" stackId="a" fill={CANCELLED} name="Cancelled" shape={segment("CANCELLED")} />
      </BarChart>
    </ResponsiveContainer>
  );
}

interface ThroughputSeries {
  trainName: string;
  buckets: { timestamp: string; count: number }[];
}

// Multi-series line chart of completed-execution throughput over the last 7 days: one line per
// top train plus an "Other" aggregate. Buckets are merged by timestamp into a row per bucket. With
// linkFor, each named train in the legend is a link.
export function ThroughputChart({
  series,
  linkFor,
}: {
  series: ThroughputSeries[];
  linkFor?: (trainName: string) => string;
}) {
  if (series.length === 0 || series.every((s) => s.buckets.every((b) => b.count === 0)))
    return <Empty>No completed runs in the last 7 days.</Empty>;

  const byTimestamp = new Map<string, Record<string, number | string>>();
  for (const s of series)
    for (const b of s.buckets) {
      const row = byTimestamp.get(b.timestamp) ?? { timestamp: b.timestamp };
      row[shortName(s.trainName)] = b.count;
      byTimestamp.set(b.timestamp, row);
    }

  const data = [...byTimestamp.values()]
    .sort((a, b) => String(a.timestamp).localeCompare(String(b.timestamp)))
    .map((r) => ({
      ...r,
      label: new Date(r.timestamp as string).toLocaleString([], {
        month: "short",
        day: "numeric",
        hour: "2-digit",
      }),
    }));

  return (
    <ResponsiveContainer width="100%" height={220}>
      <LineChart data={data} margin={{ top: 4, right: 8, left: -16, bottom: 0 }}>
        <CartesianGrid strokeDasharray="3 3" stroke={GRID} vertical={false} />
        <XAxis
          dataKey="label"
          tick={{ fill: AXIS, fontSize: 11 }}
          interval="preserveStartEnd"
          minTickGap={40}
        />
        <YAxis tick={{ fill: AXIS, fontSize: 11 }} allowDecimals={false} />
        <Tooltip contentStyle={TOOLTIP} />
        <Legend
          wrapperStyle={{ fontSize: 11 }}
          formatter={(value: string) => {
            const full = series.find((s) => shortName(s.trainName) === value)?.trainName;
            return linkFor && full && full !== "Other" ? (
              <Link to={linkFor(full)} className="hover:underline">
                {value}
              </Link>
            ) : (
              value
            );
          }}
        />
        {series.map((s, i) => (
          <Line
            key={s.trainName}
            type="monotone"
            dataKey={shortName(s.trainName)}
            stroke={SERIES_COLORS[i % SERIES_COLORS.length]}
            dot={false}
            strokeWidth={2}
          />
        ))}
      </LineChart>
    </ResponsiveContainer>
  );
}

// Horizontal bar chart for the top-N lists (failures, slowest trains).
// With linkFor, each bar is a link, named by linkLabel.
export function TopBarChart({
  rows,
  color,
  unit,
  empty,
  linkFor,
  linkLabel,
}: {
  rows: { name: string; value: number }[];
  color: string;
  unit?: string;
  empty: string;
  linkFor?: (name: string) => string;
  linkLabel?: (name: string, value: number) => string;
}) {
  if (rows.length === 0) return <Empty>{empty}</Empty>;

  const data = rows.map((r) => ({ ...r, short: shortName(r.name) }));

  return (
    <ResponsiveContainer width="100%" height={Math.max(120, data.length * 34)}>
      <BarChart
        data={data}
        layout="vertical"
        margin={{ top: 0, right: 12, left: 0, bottom: 0 }}
      >
        <XAxis type="number" hide allowDecimals={false} />
        <YAxis
          type="category"
          dataKey="short"
          width={130}
          tick={{ fill: AXIS, fontSize: 11 }}
        />
        <Tooltip
          contentStyle={TOOLTIP}
          cursor={CURSOR}
          formatter={(v) => [`${v}${unit ? ` ${unit}` : ""}`, ""]}
        />
        <Bar
          dataKey="value"
          radius={[0, 4, 4, 0]}
          shape={(props: BarShapeProps) => {
            const row = props.payload as { name: string; value: number };
            const bar = <Rectangle {...props} />;
            return linkFor ? (
              <ChartLink to={linkFor(row.name)} label={linkLabel?.(row.name, row.value) ?? shortName(row.name)}>
                {bar}
              </ChartLink>
            ) : (
              bar
            );
          }}
        >
          {data.map((d) => (
            <Cell key={d.name} fill={color} />
          ))}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  );
}

const TOOLTIP: React.CSSProperties = {
  background: "var(--surface)",
  border: "1px solid var(--line)",
  borderRadius: 8,
  fontSize: 12,
  color: "var(--fg)",
};

// What recharts passes a bar's shape: the rectangle's geometry and the row it draws.
type BarShapeProps = React.ComponentProps<typeof Rectangle> & { payload?: unknown };

// An SVG link inside a chart that navigates in the app, as a router Link does, so the key held in
// memory survives the click; a modified click still opens the URL in a new tab.
function ChartLink({ to, label, children }: { to: string; label: string; children: ReactNode }) {
  const href = useHref(to);
  const navigate = useNavigate();
  return (
    <a
      href={href}
      aria-label={label}
      className="cursor-pointer"
      onClick={(e) => {
        if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        e.preventDefault();
        navigate(to);
      }}
    >
      {children}
    </a>
  );
}

function Empty({ children }: { children: React.ReactNode }) {
  return (
    <p className="text-sm text-muted py-8 text-center">
      {children}
    </p>
  );
}

function shortName(fullName: string): string {
  const parts = fullName.split(".");
  return parts[parts.length - 1] || fullName;
}
