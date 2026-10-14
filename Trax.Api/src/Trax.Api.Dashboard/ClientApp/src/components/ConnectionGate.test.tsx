import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, test } from "vitest";
import { ConnectionGate } from "./ConnectionGate";
import { authConnectionParams, authHttpHeaders, getAuthMode } from "../lib/auth";

test("gates the app until a credential is entered, then renders children", async () => {
  render(
    <ConnectionGate>
      <div>protected content</div>
    </ConnectionGate>,
  );

  // Gated: children hidden, connect form shown.
  expect(screen.queryByText("protected content")).not.toBeInTheDocument();
  await userEvent.type(screen.getByLabelText("API key"), "admin-key-123");
  await userEvent.click(screen.getByRole("button", { name: "Connect" }));

  // Credential stored in api-key mode; children now render.
  expect(await screen.findByText("protected content")).toBeInTheDocument();
  expect(getAuthMode()).toBe("apikey");
  expect(authHttpHeaders()).toEqual({ "X-Api-Key": "admin-key-123" });
});

test("bearer mode stores a JWT and connects via authToken", async () => {
  render(
    <ConnectionGate>
      <div>protected content</div>
    </ConnectionGate>,
  );

  await userEvent.click(screen.getByRole("radio", { name: "Bearer token" }));
  await userEvent.type(screen.getByLabelText("Bearer token"), "eyJ.a.b");
  await userEvent.click(screen.getByRole("button", { name: "Connect" }));

  expect(await screen.findByText("protected content")).toBeInTheDocument();
  expect(getAuthMode()).toBe("bearer");
  expect(authConnectionParams()).toEqual({ authToken: "eyJ.a.b" });
});
