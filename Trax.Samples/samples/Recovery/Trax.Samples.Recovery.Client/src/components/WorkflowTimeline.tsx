import type { Lane, LaneSlot } from "../workflowView";

/**
 * The timeline as a fixed grid: one row per attempt, one box per junction, question, track and checkpoint, each
 * filling as the run reaches it. On a narrow screen each attempt reads top to bottom instead.
 */
export function WorkflowTimeline({ lanes }: { lanes: Lane[] }) {
  const columns = lanes[0]?.slots.length ?? 0;
  const grid = { gridTemplateColumns: `96px repeat(${columns}, minmax(0, 1fr)) 84px` };
  return (
    <div className="panel timeline">
      <div className="panel-title">Timeline</div>
      <div className="timeline-stacked">
        {lanes.map((lane) => (
          <div key={lane.id} className="stacked-lane">
            <div className="stacked-head">
              <span className="lane-name">{lane.label}</span>
              {lane.status && <StatusPill status={lane.status} />}
            </div>
            {lane.slots.map((slot) => (
              <SlotBox key={slot.key} slot={slot} />
            ))}
          </div>
        ))}
      </div>
      <div className="timeline-grid">
        {/* Room for three attempts, the most a run shows, so a requeue does not push the page down. */}
        <div className="lanes" style={{ minWidth: 96 + 84 + columns * 110 }}>
          {lanes.map((lane) => (
            <div key={lane.id} className="lane-row" style={grid}>
              <span className="lane-name">{lane.label}</span>
              {lane.slots.map((slot) => (
                <SlotBox key={slot.key} slot={slot} />
              ))}
              <span className="lane-status">{lane.status && <StatusPill status={lane.status} />}</span>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

function StatusPill({ status }: { status: NonNullable<Lane["status"]> }) {
  return <span className={`status ${status.tone}`}>{status.text}</span>;
}

function SlotBox({ slot }: { slot: LaneSlot }) {
  return (
    <div title={slot.title ?? slot.label} className={`slot ${slot.tone} ${slot.dimmed ? "dimmed" : ""}`}>
      <div className="slot-fill" style={{ width: `${slot.fill * 100}%` }} />
      <span className="slot-text">
        <span className="slot-label">{slot.label}</span>
        {slot.note && <span className="slot-note">{slot.note}</span>}
      </span>
    </div>
  );
}
