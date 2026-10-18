import { act, fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, test } from "vitest";
import { confirmAction } from "../lib/confirm";
import { ConfirmHost } from "./ConfirmHost";

describe("ConfirmHost", () => {
  test("names the action, and resolves true when it is confirmed", async () => {
    render(<ConfirmHost />);
    let answer: Promise<boolean>;
    act(() => {
      answer = confirmAction({ title: "Cancel runs", message: "Request cancellation of 3 run(s)?" });
    });
    expect(screen.getByRole("heading", { name: "Cancel runs" })).toBeInTheDocument();
    expect(screen.getByText("Request cancellation of 3 run(s)?")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "Cancel runs" }));
    await expect(answer!).resolves.toBe(true);
    expect(screen.queryByRole("heading", { name: "Cancel runs" })).toBeNull();
  });

  test("Keep it, Escape and the backdrop all decline", async () => {
    render(<ConfirmHost />);
    let a: Promise<boolean>;
    act(() => {
      a = confirmAction({ title: "Requeue run", message: "Requeue run #1?" });
    });
    fireEvent.click(screen.getByRole("button", { name: "Keep it" }));
    await expect(a!).resolves.toBe(false);

    act(() => {
      a = confirmAction({ title: "Requeue run", message: "Requeue run #2?" });
    });
    fireEvent.keyDown(window, { key: "Escape" });
    await expect(a!).resolves.toBe(false);
  });

  test("an action on everything stays disabled until the word is typed", async () => {
    render(<ConfirmHost />);
    let a: Promise<boolean>;
    act(() => {
      a = confirmAction({
        title: "Requeue all dead letters",
        message: "Requeue every unresolved dead letter?",
        danger: true,
        typeToConfirm: "all",
      });
    });
    const go = screen.getByRole("button", { name: "Requeue all dead letters" });
    expect(go).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Type all to confirm"), { target: { value: "al" } });
    expect(go).toBeDisabled();
    fireEvent.change(screen.getByLabelText("Type all to confirm"), { target: { value: "all" } });
    expect(go).toBeEnabled();
    fireEvent.click(go);
    await expect(a!).resolves.toBe(true);
  });

  test("a second request declines the first", async () => {
    render(<ConfirmHost />);
    let first: Promise<boolean>;
    act(() => {
      first = confirmAction({ title: "One", message: "first" });
    });
    act(() => {
      void confirmAction({ title: "Two", message: "second" });
    });
    await expect(first!).resolves.toBe(false);
    expect(screen.getByRole("heading", { name: "Two" })).toBeInTheDocument();
  });
});
