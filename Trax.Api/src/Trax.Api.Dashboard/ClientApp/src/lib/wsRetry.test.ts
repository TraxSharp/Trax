import { describe, expect, test } from "vitest";
import { shouldRetryWs } from "./wsRetry";

describe("shouldRetryWs", () => {
  test("retries transient closes (network drop / server restart)", () => {
    expect(shouldRetryWs({ code: 1006 })).toBe(true); // abnormal closure
    expect(shouldRetryWs({ code: 1001 })).toBe(true); // going away
    expect(shouldRetryWs({ code: 4500 })).toBe(true); // internal server error
  });

  test("does not retry auth / handshake rejections", () => {
    expect(shouldRetryWs({ code: 4401 })).toBe(false); // unauthorized
    expect(shouldRetryWs({ code: 4403 })).toBe(false); // forbidden
    expect(shouldRetryWs({ code: 4400 })).toBe(false); // bad request
    expect(shouldRetryWs({ code: 4429 })).toBe(false); // too many requests
  });

  test("retries when there is no close code (plain error / undefined)", () => {
    expect(shouldRetryWs(new Error("boom"))).toBe(true);
    expect(shouldRetryWs(undefined)).toBe(true);
    expect(shouldRetryWs(null)).toBe(true);
  });
});
