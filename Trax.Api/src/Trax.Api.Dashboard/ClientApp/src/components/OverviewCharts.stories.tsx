import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, within } from "storybook/test";
import { ExecutionsOverTimeChart, ThroughputChart, TopBarChart } from "./OverviewCharts";

const throughput = [
  { trainName: "Trax.Demo.Trains.OrderTrain", buckets: bucketsFor([3, 5, 8, 6, 9, 7]) },
  { trainName: "Trax.Demo.Trains.EmailTrain", buckets: bucketsFor([1, 2, 2, 4, 3, 5]) },
  { trainName: "Other", buckets: bucketsFor([0, 1, 0, 2, 1, 1]) },
];

function bucketsFor(counts: number[]) {
  return counts.map((count, i) => ({
    timestamp: `2026-07-0${1 + i}T00:00:00.000Z`,
    count,
  }));
}

const buckets = Array.from({ length: 12 }, (_, i) => ({
  timestamp: `2026-07-07T${String(6 + i).padStart(2, "0")}:00:00.000Z`,
  completed: 40 + ((i * 7) % 30),
  failed: (i % 4) * 3,
  cancelled: i % 5,
}));

const meta = {
  title: "Components/OverviewCharts",
  component: ExecutionsOverTimeChart,
  args: { buckets },
} satisfies Meta<typeof ExecutionsOverTimeChart>;

export default meta;
type Story = StoryObj<typeof meta>;

export const ExecutionsOverTime: Story = {
  render: () => (
    <div className="w-[640px]">
      <ExecutionsOverTimeChart buckets={buckets} />
    </div>
  ),
};

export const TopFailures: Story = {
  render: () => (
    <div className="w-[480px]">
      <TopBarChart
        color="var(--chart-failed)"
        empty="No failures"
        rows={[
          { name: "Trax.Demo.Trains.OrderTrain", value: 42 },
          { name: "Trax.Demo.Trains.EmailTrain", value: 17 },
          { name: "Trax.Demo.Trains.ReportTrain", value: 5 },
        ]}
      />
    </div>
  ),
};

export const SlowestTrains: Story = {
  render: () => (
    <div className="w-[480px]">
      <TopBarChart
        color="var(--chart-series-1)"
        unit="ms"
        empty="No completed runs"
        rows={[
          { name: "Trax.Demo.Trains.ReportTrain", value: 1830 },
          { name: "Trax.Demo.Trains.OrderTrain", value: 640 },
          { name: "Trax.Demo.Trains.EmailTrain", value: 120 },
        ]}
      />
    </div>
  ),
};

export const Throughput: Story = {
  render: () => (
    <div className="w-[640px]">
      <ThroughputChart series={throughput} />
    </div>
  ),
};

export const ThroughputEmpty: Story = {
  render: () => (
    <div className="w-[640px]">
      <ThroughputChart series={[]} />
    </div>
  ),
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(c.getByText(/No completed runs/)).toBeInTheDocument();
  },
};

export const Empty: Story = {
  render: () => (
    <div className="w-[640px]">
      <ExecutionsOverTimeChart buckets={[]} />
    </div>
  ),
};
