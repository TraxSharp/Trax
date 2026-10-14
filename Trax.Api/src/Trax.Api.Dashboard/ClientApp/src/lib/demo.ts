// True in the demo build (`vite build --mode demo`, which sets VITE_DEMO=1): the dashboard answers
// from recordings, with no API behind it. A literal at build time, so the normal build drops every
// demo-only branch.
export const isDemo = import.meta.env.VITE_DEMO === "1";

