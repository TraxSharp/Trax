import { fireEvent, render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { Provider } from "urql";
import { describe, expect, test } from "vitest";
import { createMockClient, createMockStore } from "../mock";
import { lookupScenario, MACHINE_IDS } from "../mock/scenarios";
import { Search } from "./Search";

function Where() {
  const { pathname, search } = useLocation();
  return <output data-testid="where">{pathname + search}</output>;
}

function renderSearch() {
  const client = createMockClient({ store: createMockStore(), fixtures: false, overrides: lookupScenario });
  render(
    <Provider value={client}>
      <MemoryRouter initialEntries={["/"]}>
        <Search />
        <Routes>
          <Route path="*" element={<Where />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

const box = () => screen.getByRole("textbox", { name: "Find by id, external id, subject or name" });

describe("Search", () => {
  test("a run's id opens the run", async () => {
    renderSearch();
    fireEvent.change(box(), { target: { value: "902" } });
    fireEvent.submit(box());
    expect(await screen.findByText("/runs/902")).toBeInTheDocument();
  });

  test("an instance's GUID opens the instance", async () => {
    renderSearch();
    fireEvent.change(box(), { target: { value: MACHINE_IDS.dispatched } });
    fireEvent.submit(box());
    expect(await screen.findByText(new RegExp(`^/machines/.+/${MACHINE_IDS.dispatched}`))).toBeInTheDocument();
  });

  test("text nothing has says so and goes nowhere", async () => {
    renderSearch();
    fireEvent.change(box(), { target: { value: "nothing-has-this" } });
    fireEvent.submit(box());
    expect(await screen.findByText("Nothing has that id, key or name.")).toBeInTheDocument();
    expect(screen.getByTestId("where").textContent).toBe("/");
  });

  test("/ focuses it from anywhere on the page", () => {
    renderSearch();
    fireEvent.keyDown(window, { key: "/" });
    expect(document.activeElement).toBe(box());
  });
});
