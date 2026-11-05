import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, test, vi } from "vitest";
import { PersistedOperationEditor } from "./PersistedOperationEditor";

// The upload answers with refusals whose codes come from the server.
const codes = vi.hoisted(() => ({ current: [] as string[] }));
vi.mock("../lib/consoleClient", () => ({
  useTraxMutation: () => [
    { fetching: false },
    async () => ({
      data: {
        persistedOperations: {
          uploadPersistedOperation: {
            success: false,
            errors: codes.current.map((code) => ({ code, message: `Refused as ${code}.` })),
          },
        },
      },
    }),
  ],
}));

describe("PersistedOperationEditor", () => {
  test("titles a refusal it knows, and shows any other code as itself, an Object member's name among them", async () => {
    codes.current = ["PARSE_FAILED", "constructor", "__proto__", "toString"];
    render(<PersistedOperationEditor onSaved={() => {}} onClose={() => {}} />);

    fireEvent.change(screen.getByLabelText("Id"), { target: { value: "greet.v1" } });
    fireEvent.change(screen.getByLabelText("Document"), { target: { value: "query Greet { hello }" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    const alerts = await screen.findAllByRole("alert");
    expect(alerts.map((a) => a.querySelector("strong")?.textContent)).toEqual([
      "Parse error",
      "constructor",
      "__proto__",
      "toString",
    ]);
  });
});
