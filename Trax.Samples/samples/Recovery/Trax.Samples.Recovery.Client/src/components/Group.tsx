/** A labelled group of controls. */
export function Group({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="group">
      <span className="group-label">{label}</span>
      {children}
    </div>
  );
}
