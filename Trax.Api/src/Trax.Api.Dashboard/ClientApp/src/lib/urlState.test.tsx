import { act, render, screen } from "@testing-library/react";
import { MemoryRouter, useLocation } from "react-router-dom";
import { describe, expect, test } from "vitest";
import { useUrlState, useUrlStateOrNull } from "./urlState";

type Setters = { train: (v: string) => void; order: (v: "NEWEST" | "OLDEST") => void; machine: (v: string | null) => void };
const setters = {} as Setters;

function Probe() {
  const [train, setTrain] = useUrlState("train");
  const [order, setOrder] = useUrlState<"NEWEST" | "OLDEST">("order", "NEWEST");
  const [machine, setMachine] = useUrlStateOrNull("machine");
  Object.assign(setters, { train: setTrain, order: setOrder, machine: setMachine });
  const { search } = useLocation();
  return (
    <output data-testid="probe">
      {JSON.stringify({ train, order, machine, search })}
    </output>
  );
}

const read = () => JSON.parse(screen.getByTestId("probe").textContent!);

function renderAt(url: string) {
  render(
    <MemoryRouter initialEntries={[url]}>
      <Probe />
    </MemoryRouter>,
  );
}

describe("useUrlState", () => {
  test("a filter opened from a link is the one the page shows", () => {
    renderAt("/runs?train=Billing&order=OLDEST&machine=approval");
    expect(read()).toMatchObject({ train: "Billing", order: "OLDEST", machine: "approval" });
  });

  test("a default stays out of the URL", () => {
    renderAt("/runs");
    expect(read()).toMatchObject({ train: "", order: "NEWEST", machine: null, search: "" });
    act(() => setters.order("OLDEST"));
    expect(read().search).toBe("?order=OLDEST");
    act(() => setters.order("NEWEST"));
    expect(read().search).toBe("");
  });

  test("several filters set in one handler all land", () => {
    renderAt("/runs?order=OLDEST");
    act(() => {
      setters.train("Billing");
      setters.machine("approval");
    });
    expect(read()).toMatchObject({ train: "Billing", machine: "approval", order: "OLDEST" });
    act(() => setters.machine(null));
    expect(read().machine).toBeNull();
    expect(read().search).toBe("?order=OLDEST&train=Billing");
  });
});
