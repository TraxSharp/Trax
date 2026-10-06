import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, waitFor, within } from "storybook/test";
import { ClusterPage } from "./ClusterPage";
import { clusterScenario } from "../mock/scenarios";

const meta = {
  title: "Pages/Cluster",
  component: ClusterPage,
  parameters: { route: "/cluster", mock: clusterScenario },
} satisfies Meta<typeof ClusterPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Mock: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("trax-api-1")).toBeInTheDocument());
    // The recent host is Live; the 8-minutes-idle scheduler is Stale.
    expect(c.getByText("Live")).toBeInTheDocument();
    expect(c.getByText("Stale")).toBeInTheDocument();
  },
};

export const Empty: Story = {
  parameters: {
    mock: { resolvers: () => ({ OperationsQueries: { hosts: () => [] } }) },
  },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() =>
      expect(c.getByText(/No hosts have executed trains yet/)).toBeInTheDocument(),
    );
  },
};

export const Real: Story = { parameters: { real: true } };
