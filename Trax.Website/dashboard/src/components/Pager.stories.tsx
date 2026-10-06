import type { Meta, StoryObj } from "@storybook/react-vite";
import { expect, fn, userEvent, within } from "storybook/test";
import { Pager } from "./Pager";

const noop = () => {};

const meta = {
  title: "Components/Pager",
  component: Pager,
  args: {
    total: 3_000_000,
    isEstimated: true,
    isFirstPage: true,
    nextCursor: 2_999_976,
    onPrev: noop,
    onNext: noop,
  },
} satisfies Meta<typeof Pager>;

export default meta;
type Story = StoryObj<typeof meta>;

// First page: Previous disabled, Next enabled, estimated total.
export const FirstPage: Story = {};

// A middle page: both buttons enabled, exact total.
export const MiddlePage: Story = {
  args: { isFirstPage: false, isEstimated: false, total: 1_240 },
};

// Last page: no next cursor, so Next is disabled.
export const LastPage: Story = {
  args: { isFirstPage: false, isEstimated: false, total: 1_240, nextCursor: null },
};

// On the first page Previous is disabled; Next fires onNext with the current cursor.
export const FirstPageDisablesPrevious: Story = {
  args: { isFirstPage: true, onPrev: fn(), onNext: fn() },
  play: async ({ canvasElement, args }) => {
    const c = within(canvasElement);
    expect(c.getByRole("button", { name: "Previous" })).toBeDisabled();
    const next = c.getByRole("button", { name: "Next" });
    expect(next).toBeEnabled();
    await userEvent.click(next);
    expect(args.onNext).toHaveBeenCalledWith(2_999_976);
    expect(args.onPrev).not.toHaveBeenCalled();
  },
};

// On the last page Next is disabled and clicking it is a no-op; Previous still works.
export const LastPageDisablesNext: Story = {
  args: { isFirstPage: false, nextCursor: null, onPrev: fn(), onNext: fn() },
  play: async ({ canvasElement, args }) => {
    const c = within(canvasElement);
    const next = c.getByRole("button", { name: "Next" });
    expect(next).toBeDisabled();
    await userEvent.click(next); // disabled: guarded, must not fire
    expect(args.onNext).not.toHaveBeenCalled();
    await userEvent.click(c.getByRole("button", { name: "Previous" }));
    expect(args.onPrev).toHaveBeenCalled();
  },
};

// A middle page enables both directions.
export const MiddlePageBothEnabled: Story = {
  args: { isFirstPage: false, nextCursor: 1_500_000, onPrev: fn(), onNext: fn() },
  play: async ({ canvasElement, args }) => {
    const c = within(canvasElement);
    await userEvent.click(c.getByRole("button", { name: "Next" }));
    await userEvent.click(c.getByRole("button", { name: "Previous" }));
    expect(args.onNext).toHaveBeenCalledWith(1_500_000);
    expect(args.onPrev).toHaveBeenCalled();
  },
};

// total undefined renders no count label (still no crash, buttons present).
export const NoTotal: Story = {
  args: { total: undefined, isFirstPage: true, nextCursor: 100 },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    expect(c.getByRole("button", { name: "Next" })).toBeEnabled();
    expect(c.queryByText(/total/)).not.toBeInTheDocument();
  },
};

// A text-filtered log count stops at 10,000: the pager says "10,000+", never an estimate.
export const CappedCount: Story = {
  args: { isEstimated: false, isCapped: true, total: 10_000 },
  play: async ({ canvasElement }) => {
    const c = within(canvasElement);
    await expect(c.getByText("10,000+ total")).toBeInTheDocument();
  },
};
