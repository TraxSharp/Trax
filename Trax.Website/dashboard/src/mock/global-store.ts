import type { MockStore } from "./store/mock-store";

// Standalone, dependency-free accessor for the store the mock client pins on
// globalThis.__mockStore. Kept out of the barrel so app code (the Layout nav, the viewer)
// can check for an active mock without pulling graphql-tools / the SDL into the main bundle.
// The MockStore import is type-only, so it erases at runtime.
export function getGlobalMockStore(): MockStore | undefined {
  return (globalThis as { __mockStore?: MockStore }).__mockStore;
}
