import { render, screen } from "@testing-library/react";
import { MemoryRouter, useLocation } from "react-router-dom";
import { describe, expect, test } from "vitest";
import { Provider } from "urql";
import { AppRoutes } from "../AppRoutes";
import { createMockClient } from "../mock/client";

// The console's routes moved to its navigation (api/0043). A link from before the move still lands.
function Where() {
  const { pathname, search } = useLocation();
  return <output data-testid="where">{pathname + search}</output>;
}

function renderAt(path: string) {
  render(
    <Provider value={createMockClient()}>
      <MemoryRouter initialEntries={[path]}>
        <AppRoutes />
        <Where />
      </MemoryRouter>
    </Provider>,
  );
}

describe("moved routes", () => {
  test.each([
    ["/executions/42", "/runs/42"],
    ["/work-queue/7?x=1", "/runs/queued/7?x=1"],
    ["/dead-letters?manifestId=801", "/runs/dead-letters?manifestId=801"],
    ["/state-machines/approval/system/abc", "/machines/approval/system/abc"],
    ["/manifests/5", "/schedules/5"],
    ["/groups/3", "/schedules/groups/3"],
    ["/cluster", "/system/hosts"],
    ["/settings/server", "/system/scheduler"],
    ["/settings/user", "/preferences"],
    ["/persisted-operations/greet.v1?tenant=acme", "/api-admin/persisted-operations/greet.v1?tenant=acme"],
  ])("%s lands on %s", async (from, to) => {
    renderAt(from);
    expect((await screen.findByTestId("where")).textContent).toBe(to);
  });
});

describe("navigation", () => {
  test("the sidebar names what an operator works with, not the tables", async () => {
    renderAt("/");
    for (const name of ["Overview", "All runs", "Queued", "Dead letters", "Trains", "Manifests", "Hosts", "Scheduler"])
      expect((await screen.findAllByRole("link", { name })).length).toBeGreaterThan(0);
    expect(screen.queryByRole("link", { name: "Runs" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Data" })).toBeNull();
  });

  test("the Queued view marks Queued, not All runs", async () => {
    renderAt("/runs/queued");
    const queued = (await screen.findAllByRole("link", { name: "Queued" }))[0];
    expect(queued.className).toContain("bg-accent-soft");
    expect(screen.getAllByRole("link", { name: "All runs" })[0].className).not.toContain("bg-accent-soft");
  });
});
