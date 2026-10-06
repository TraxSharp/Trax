import { render, screen } from "@testing-library/react";
import { expect, test } from "vitest";
import { DemoBanner } from "./DemoBanner";

// The banner is the only thing that says the demo is a demo, framed by the site or opened on its own.
test("says the data is recorded and changes are simulated", () => {
  render(<DemoBanner />);
  expect(screen.getByRole("note")).toHaveTextContent("recorded data, changes are simulated");
});
