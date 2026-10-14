import { gql } from "@urql/core";
import { afterEach, describe, expect, test, vi } from "vitest";
import { client } from "./graphql";

afterEach(() => vi.restoreAllMocks());

describe("the real GraphQL client", () => {
  // Trax refuses GraphQL over GET unless the host opts in, so a query sent as GET fails with
  // "[Network] Not Found" against every real host.
  test("sends queries as POST", async () => {
    const fetchSpy = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValue(
        new Response(JSON.stringify({ data: { operations: null } }), {
          headers: { "Content-Type": "application/json" },
        }),
      );

    await client
      .query(gql`query Probe { operations { __typename } }`, {}, { requestPolicy: "network-only" })
      .toPromise();

    expect(fetchSpy).toHaveBeenCalled();
    const init = fetchSpy.mock.calls[0][1] as RequestInit;
    expect(init.method).toBe("POST");
  });
});
