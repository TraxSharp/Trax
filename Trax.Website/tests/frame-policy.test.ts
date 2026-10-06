// Checks the framing headers in next.config.ts. A browser enforces the policy of
// the document it first loaded, so a client-side navigation to /dashboard from
// any page runs under that page's policy: every page must allow framing this
// site's own pages, or the dashboard's frame of its demo is blocked until a
// reload. Being framed must stay refused everywhere but the demo.

import { test } from "node:test";
import assert from "node:assert/strict";
import nextConfig from "../next.config.ts";

type Header = { key: string; value: string };
type Rule = { source: string; headers: Header[] };

async function rules(): Promise<Rule[]> {
  return (await nextConfig.headers!()) as Rule[];
}

function directive(rule: Rule, name: string): string | undefined {
  const csp = rule.headers.find((h) => h.key === "Content-Security-Policy")?.value ?? "";
  return csp
    .split(";")
    .map((d) => d.trim())
    .find((d) => d.startsWith(`${name} `));
}

const isDemo = (rule: Rule) => rule.source.startsWith("/dashboard/demo");

test("every page may frame this site's own pages", async () => {
  for (const rule of (await rules()).filter((r) => !isDemo(r)))
    assert.equal(directive(rule, "frame-src"), "frame-src 'self'", rule.source);
});

test("no page but the demo may be framed", async () => {
  for (const rule of (await rules()).filter((r) => !isDemo(r))) {
    assert.equal(directive(rule, "frame-ancestors"), "frame-ancestors 'none'", rule.source);
    assert.equal(rule.headers.find((h) => h.key === "X-Frame-Options")?.value, "DENY", rule.source);
  }
});

test("the demo may be framed only by this site", async () => {
  const demo = (await rules()).filter(isDemo);
  assert.ok(demo.length > 0);
  for (const rule of demo)
    assert.equal(directive(rule, "frame-ancestors"), "frame-ancestors 'self'", rule.source);
});
