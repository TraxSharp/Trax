// The description a docs page falls back to (summarize in src/lib/docs.ts): plain text, so it
// never carries an angle bracket, whatever markup the first paragraph holds.
import { test } from "node:test";
import assert from "node:assert/strict";
import { summarize } from "../src/lib/docs.ts";

test("strips tags and links from the first paragraph", () => {
  assert.equal(summarize("A [train](x.md) runs **junctions** in `order`.<br/>"), "A train runs junctions in order.");
});

test("leaves no angle bracket when stripping would rebuild a tag", () => {
  for (const input of ["Text <scr<b>ipt>alert(1)</script> end.", "a <<b>img src=x onerror=y>> b", "a < b and c > d."]) {
    assert.doesNotMatch(summarize(input), /[<>]/, input);
  }
});
