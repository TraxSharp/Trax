import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, waitFor, within } from "storybook/test";
import { ClusterPage } from "./ClusterPage";
import { clusterScenario } from "../mock/scenarios";

const meta = {
  title: "Pages/Hosts",
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
    expect(c.getByText("3 of 8")).toBeInTheDocument();
    expect(c.getByText("No local workers")).toBeInTheDocument();
    expect(c.getByText("Manifest manager, Job dispatcher")).toBeInTheDocument();
    // Running now links to the runs in progress on that host.
    expect(c.getByRole("link", { name: "3" })).toHaveAttribute(
      "href",
      "/runs?state=IN_PROGRESS&host=trax-scheduler-1",
    );
    expect(c.queryByText(/No online host runs the job dispatcher/)).not.toBeInTheDocument();
    // The dashboard's own process stats, moved here from the Overview.
    const own = c.getByRole("region", { name: "This dashboard's process" });
    await waitFor(() => expect(own).toHaveTextContent("4.2%"));
    expect(own).toHaveTextContent("412 MB");
    expect(own).toHaveTextContent("96 MB");
    expect(own).toHaveTextContent("1d 2h");
  },
};

// Only the API host is online: nothing starts queued work, and the page says so.
export const NoDispatcherOnline: Story = {
  parameters: {
    mock: {
      resolvers: () => {
        const base = (clusterScenario.resolvers as () => { OperationsQueries: { hosts: () => Record<string, unknown>[] } })();
        return {
          ...base,
          OperationsQueries: {
            hosts: () =>
              base.OperationsQueries.hosts().map((h) => ({ ...h, online: !h.runsDispatcher })),
          },
        };
      },
    },
  },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText(/No online host runs the job dispatcher/)).toBeInTheDocument();
  },
};

export const Empty: Story = {
  parameters: {
    mock: {
      resolvers: () => ({
        OperationsQueries: { hosts: () => [] },
        ConfigQueries: { scheduler: () => ({ manifestManagerEnabled: true, jobDispatcherEnabled: true }) },
      }),
    },
  },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await waitFor(() =>
      expect(c.getByText(/No host has sent a heartbeat in the last day/)).toBeInTheDocument(),
    );
    expect(c.getByText(/No online host runs the job dispatcher/)).toBeInTheDocument();
  },
};

export const Real: Story = { parameters: { real: true } };

// A paused dispatcher beats as not running it; the page says it is paused rather than missing,
// and links to where it is resumed.
export const DispatcherPaused: Story = {
  parameters: {
    mock: {
      resolvers: () => {
        const base = (clusterScenario.resolvers as () => { OperationsQueries: { hosts: () => Record<string, unknown>[] } })();
        return {
          ...base,
          OperationsQueries: { hosts: () => base.OperationsQueries.hosts().map((h) => ({ ...h, runsDispatcher: false })) },
          ConfigQueries: { scheduler: () => ({ manifestManagerEnabled: true, jobDispatcherEnabled: false }) },
        };
      },
    },
  },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(await c.findByText(/The job dispatcher is paused/)).toBeInTheDocument();
    expect(c.getByRole("link", { name: "Resume it on the Scheduler page" })).toHaveAttribute("href", "/system/scheduler");
    expect(c.queryByText(/No online host runs the job dispatcher/)).not.toBeInTheDocument();
  },
};
