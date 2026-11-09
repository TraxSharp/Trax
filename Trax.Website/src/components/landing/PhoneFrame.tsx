// The phone every demo draws a person's app in: a bezel, a status bar with the camera cut-out in the middle, the app's
// screen, and the home bar. One frame for every sample, so the phones look the same wherever they appear.
export default function PhoneFrame({
  children,
  height = "h-[500px]",
  ring,
}: {
  children: React.ReactNode;
  /** The phone's height, as a Tailwind class; the width is fixed. */
  height?: string;
  /** A colour to outline the phone in while its person is acting, or none. */
  ring?: string | null;
}) {
  return (
    <div
      className={`mx-auto flex w-full max-w-[264px] flex-none flex-col rounded-[40px] border-[7px] border-[#2c2d2a] bg-[#0b0c0a] p-2 transition-shadow duration-500 ${height}`}
      style={{ boxShadow: ring ? `0 0 0 2px ${ring}66, 0 0 24px ${ring}22` : "0 0 0 1px #1f201e" }}
    >
      <div className="relative flex min-h-0 flex-1 flex-col overflow-hidden rounded-[32px] bg-bg-secondary">
        <div className="flex h-9 flex-none items-center justify-between px-6 font-mono text-[11px] text-text-secondary">
          <span>9:41</span>
          <span aria-hidden="true" className="absolute left-1/2 top-2 h-[22px] w-[84px] -translate-x-1/2 rounded-full bg-black" />
          <span aria-hidden="true" className="flex items-center gap-1">
            <span className="flex items-end gap-[2px]">
              {[4, 6, 8, 10].map((h) => (
                <span key={h} className="w-[3px] rounded-sm bg-text-secondary" style={{ height: h }} />
              ))}
            </span>
            <span className="ml-1 h-[9px] w-[18px] rounded-[3px] border border-text-secondary p-[1px]">
              <span className="block h-full w-3/4 rounded-[1px] bg-text-secondary" />
            </span>
          </span>
        </div>
        <div className="flex min-h-0 flex-1 flex-col">{children}</div>
        <div aria-hidden="true" className="mx-auto mb-2 mt-1 h-1 w-24 flex-none rounded-full bg-text-muted/60" />
      </div>
    </div>
  );
}
