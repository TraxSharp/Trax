import type { ComponentType } from "react";
import { composeStories } from "@storybook/react-vite";
import { cleanup, render } from "@testing-library/react";
import { afterEach, describe, expect, test } from "vitest";

// Render every story against the mock and run its play function, so a broken story (or a
// component that throws under the mock) fails CI. Stories that hit the live devhost
// (`parameters.real`) are skipped — no backend in CI.
type StoryModule = Parameters<typeof composeStories>[0];
type PlayFn = (ctx: { canvasElement: HTMLElement }) => Promise<void> | void;
type ComposedStory = ComponentType & { parameters?: { real?: boolean }; play?: PlayFn };

const modules = import.meta.glob("../**/*.stories.tsx", { eager: true });

afterEach(cleanup);

for (const [path, mod] of Object.entries(modules)) {
  const composed = composeStories(mod as StoryModule);
  describe(path.replace("../", ""), () => {
    for (const [name, Story] of Object.entries(composed) as [string, ComposedStory][]) {
      if (Story.parameters?.real) continue;
      test(`${name} renders`, async () => {
        const { container } = render(<Story />);
        if (Story.play) await Story.play({ canvasElement: container });
        expect(container.firstChild).toBeTruthy();
      });
    }
  });
}
