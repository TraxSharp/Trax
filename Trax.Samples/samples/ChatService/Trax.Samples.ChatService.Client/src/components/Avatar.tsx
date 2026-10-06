const TONES = ["moss", "emerald", "amber", "rose", "sky", "lichen"];

/** A round initial for a person or room, tinted the same way every time for the same name. */
export function Avatar({ name, size = "md" }: { name: string; size?: "sm" | "md" | "lg" }) {
  let hash = 2166136261; // FNV-1a, which spreads short names across the tones
  for (const ch of name) hash = Math.imul(hash ^ ch.charCodeAt(0), 16777619) >>> 0;
  const tone = TONES[hash % TONES.length];
  return (
    <span className={`avatar avatar-${size} tone-${tone}`} aria-hidden="true">
      {name.trim().charAt(0).toUpperCase() || "?"}
    </span>
  );
}
