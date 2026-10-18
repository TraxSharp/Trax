import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, waitFor, within } from "storybook/test";
import { ClusterPage } from "./ClusterPage";
import { clusterScenario } from "../mock/scenarios";

const meta = {
  title: "Pages/Cluster",
  component: ClusterPage,
  parameters: { route: "/system/hosts", mock: clusterScenario },
} satisfies Meta<typeof ClusterPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Mock: Story = {
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() => expect(c.getByText("trax-api-1")).toBeInTheDocument());
    // The scheduler beat 5 seconds ago and is online; the API host stopped 8 minutes ago.
    expect(c.getByText("Online")).toBeInTheDocument();
    expect(c.getByText("Offline")).toBeInTheDocument();
    expect(c.getByText("3 / 8")).toBeInTheDocument();
    expect(c.queryByText(/No online host runs the dispatcher/)).not.toBeInTheDocument();
  },
};

export const Empty: Story = {
  parameters: {
    mock: { resolvers: () => ({ OperationsQueries: { hosts: () => [] } }) },
  },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() =>
      expect(c.getByText(/No host has sent a heartbeat in the last day/)).toBeInTheDocument(),
    );
  },
};

export const Real: Story = { parameters: { real: true } };
