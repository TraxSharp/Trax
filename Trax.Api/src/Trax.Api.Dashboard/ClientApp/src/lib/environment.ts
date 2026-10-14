// The badge colours the Blazor header uses: blue for Development, red for Production, grey for
// anything else.
export function environmentBadgeColor(name: string): string {
  const n = name.toLowerCase();
  if (n === "development") return "#1976D2";
  if (n === "production") return "#D32F2F";
  return "#757575";
}
