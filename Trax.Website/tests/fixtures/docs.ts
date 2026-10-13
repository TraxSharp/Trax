// DocPage fixtures for the sidebar and page-chrome tests: pages with the
// Diátaxis front matter (type, area, source, ...) and pages without it.
import type { DocPage } from "../../src/lib/docs";

export function page(slug: string, fields: Partial<DocPage> = {}): DocPage {
  return {
    slug,
    title: fields.title ?? slug.split("/").pop() ?? slug,
    navOrder: 999,
    hasChildren: false,
    filePath: `/cache/${slug || "index"}.md`,
    sourcePath: `${slug || "index"}.md`,
    body: `# ${fields.title ?? slug}\n\nBody.\n`,
    description: "",
    source: [],
    redirectFrom: [],
    ...fields,
  };
}

/** A small docs set with the new front matter. */
export function diataxisDocs(): DocPage[] {
  return [
    page("", { title: "Home", type: "landing", area: "general", navOrder: 1 }),
    page("about-these-docs", { title: "About these docs", type: "explanation", area: "contributing" }),
    page("tutorials/first-train", { title: "Your first train", type: "tutorial", area: "trains", navOrder: 1 }),
    page("tutorials/first-schedule", { title: "Your first schedule", type: "tutorial", area: "scheduling", navOrder: 1 }),
    page("how-to/resolve-dead-letter", { title: "Resolve a dead letter", type: "how-to", area: "scheduling", navOrder: 2 }),
    page("how-to/retry-a-job", { title: "Retry a job", type: "how-to", area: "scheduling", navOrder: 2 }),
    page("how-to/add-a-junction", { title: "Add a junction", type: "how-to", area: "trains", navOrder: 5 }),
    page("how-to/secure-graphql", { title: "Secure a GraphQL host", type: "how-to", area: "security" }),
    page("how-to/untyped-area", { title: "Untyped area page", type: "how-to" }),
    page("explanation/scheduling", { title: "How scheduling works", type: "explanation", area: "scheduling" }),
    page("explanation/scheduling/manifests", {
      title: "Manifests",
      type: "explanation",
      area: "scheduling",
      parent: "How scheduling works",
    }),
    page("reference/glossary", { title: "Glossary", type: "reference", area: "general" }),
    page("sdk-reference", { title: "SDK Reference", type: "landing", section: "SDK Reference", navOrder: 13 }),
    page("sdk-reference/scheduler", {
      title: "Scheduler API",
      type: "landing",
      parent: "SDK Reference",
      area: "scheduling",
    }),
    page("sdk-reference/scheduler/schedule", {
      title: "Schedule",
      type: "reference",
      area: "scheduling",
      parent: "Scheduler API",
      grandParent: "SDK Reference",
      package: "Trax.Scheduler",
      namespace: "Trax.Scheduler.Services",
      source: ["Trax.Scheduler/src/Trax.Scheduler/Services/ITraxScheduler.cs"],
    }),
    page("sdk-reference/core", {
      title: "Core API",
      type: "landing",
      section: "SDK Reference",
      area: "trains",
      navOrder: 14,
    }),
    page("samples/chat", { title: "Chat service", type: "tutorial", area: "samples" }),
    page("contributing/writing-docs", { title: "Writing docs", type: "how-to", area: "contributing" }),
  ];
}

/** Today's docs: section/parent/grand_parent only, no type or area. */
export function legacyDocs(): DocPage[] {
  return [
    page("", { title: "Home", navOrder: 1 }),
    page("getting-started", { title: "Getting Started", navOrder: 2 }),
    page("core", { title: "Core", section: "Packages", navOrder: 3 }),
    page("core/memory", { title: "Memory", parent: "Core", navOrder: 3 }),
    page("core/chains", { title: "Building Chains", parent: "Core", navOrder: 2 }),
    page("cross-cutting", { title: "Cross-Cutting", section: "Guides", navOrder: 11 }),
    page("cross-cutting/di", { title: "DI Registration", parent: "Cross-Cutting", navOrder: 6 }),
    page("cross-cutting/di/trains", {
      title: "Train Registration",
      parent: "DI Registration",
      grandParent: "Cross-Cutting",
      navOrder: 1,
    }),
    page("reference", { title: "Reference", section: "Reference", navOrder: 12 }),
    page("sdk-reference", { title: "SDK Reference", section: "SDK Reference", navOrder: 13 }),
  ];
}
