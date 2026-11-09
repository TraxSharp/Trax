// The demos' hues, as the CSS variables globals.css defines for each theme, and translucency over any colour. A
// demo names a hue here rather than a hex value, so the light theme can darken it enough to read.

export const HUE = {
  green: "var(--hue-green)",
  greenDeep: "var(--hue-green-deep)",
  amber: "var(--hue-amber)",
  blue: "var(--hue-blue)",
  red: "var(--hue-red)",
  redSoft: "var(--hue-red-soft)",
  violet: "var(--hue-violet)",
  pink: "var(--hue-pink)",
  teal: "var(--hue-teal)",
  coral: "var(--hue-coral)",
  gold: "var(--hue-gold)",
  copper: "var(--hue-copper)",
  steel: "var(--hue-steel)",
  slate: "var(--hue-slate)",
  rose: "var(--hue-rose)",
  grey: "var(--hue-grey)",
  olive: "var(--hue-olive)",
  butter: "var(--hue-butter)",
} as const;

/** Lines and quiet text that are not a hue: the theme's own. */
export const NEUTRAL = {
  line: "var(--border)",
  lineStrong: "var(--border-hover)",
  muted: "var(--text-muted)",
  text: "var(--text-primary)",
} as const;

/** `color` at `percent` opacity, for a fill or a glow over whatever is behind it. */
export const alpha = (color: string, percent: number) => `color-mix(in srgb, ${color} ${percent}%, transparent)`;
