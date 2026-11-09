// The environment badge's tone, in the theme's own colours: Development in the info blue, Production in the danger
// red, so it is not mistaken for a safe one, and anything else neutral. Each is a soft fill with a line and text of
// its hue, as the site draws its chips.
export type EnvironmentTone = "development" | "production" | "other";

export function environmentTone(name: string): EnvironmentTone {
  const n = name.toLowerCase();
  if (n === "development") return "development";
  if (n === "production") return "production";
  return "other";
}

export const ENVIRONMENT_BADGE: Record<EnvironmentTone, string> = {
  development: "bg-info-soft border-info-line text-info-fg",
  production: "bg-danger-soft border-danger-line text-danger-fg",
  other: "bg-raised border-line-strong text-fg-2",
};
