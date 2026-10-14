import type { Meta, StoryObj } from "@storybook/react-vite";
import { ExceptionViewer } from "./detail";

const meta = {
  title: "Components/ExceptionViewer",
  component: ExceptionViewer,
  args: {
    reason: "Validation failed: order total must be positive",
    junction: "ValidateOrder",
    stackTrace:
      "at Trax.Demo.Trains.ValidateOrder.RunAsync()\n  at Trax.Effect.ServiceTrain.Run()\n  at Trax.Scheduler.JobRunner.Execute()",
  },
} satisfies Meta<typeof ExceptionViewer>;

export default meta;
type Story = StoryObj<typeof meta>;

export const WithStackTrace: Story = {};
export const ReasonOnly: Story = { args: { junction: null, stackTrace: null } };
