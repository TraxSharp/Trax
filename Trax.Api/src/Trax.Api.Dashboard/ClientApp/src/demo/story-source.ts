import type { ServeListener } from "./client";
import type { Recordings } from "./recordings";

// A switch for the Storybook harness (.storybook/preview.tsx): while it holds recordings, every story
// renders against the demo's recording client instead of the auto-mock, so a test can run the
// stories' play functions on recorded data (see demo-stories.test.tsx). Off by default.

interface StorySource {
  recordings: Recordings;
  onServe?: ServeListener;
}

let source: StorySource | null = null;

export function setStoryRecordings(next: StorySource | null): void {
  source = next;
}

export function storyRecordings(): StorySource | null {
  return source;
}
