import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Legend,
  Line,
  LineChart,
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

// Stacked bar chart of execution outcomes over the selected window.
export function ExecutionsOverTimeChart({ buckets }: { buckets: Bucket[] }) {
  const data = buckets.map((b) => ({
    ...b,
    label: new Date(b.timestamp).toLocaleTimeString([], {
      hour: "2-digit",
      minute: "2-digit",
    }),
  }));

  if (data.every((d) => d.completed + d.failed + d.cancelled === 0))
    return <Empty>No executions in this window.</Empty>;

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
        <Bar dataKey="completed" stackId="a" fill={COMPLETED} name="Completed" />
        <Bar dataKey="failed" stackId="a" fill={FAILED} name="Failed" />
        <Bar dataKey="cancelled" stackId="a" fill={CANCELLED} name="Cancelled" />
      </BarChart>
    </ResponsiveContainer>
  );
}

interface ThroughputSeries {
  trainName: string;
  buckets: { timestamp: string; count: number }[];
}

// Multi-series line chart of completed-execution throughput over the last 7 days: one line per
// top train plus an "Other" aggregate. Buckets are merged by timestamp into a row per bucket.
export function ThroughputChart({ series }: { series: ThroughputSeries[] }) {
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
        <Legend wrapperStyle={{ fontSize: 11 }} />
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
export function TopBarChart({
  rows,
  color,
  unit,
  empty,
}: {
  rows: { name: string; value: number }[];
  color: string;
  unit?: string;
  empty: string;
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
        <Bar dataKey="value" radius={[0, 4, 4, 0]}>
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
