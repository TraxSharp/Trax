import { isBooleanProperty, isEnumProperty } from "../lib/trainInput";
import { shortName } from "../lib/format";
import type { TrainInputFormState } from "../lib/useTrainInputForm";

// The train picker and input form the Queue and Run dialogs share: a train dropdown, one field per
// input property (a dropdown for an enum, a checkbox for a boolean, text otherwise), and a raw JSON
// box that overrides the fields. State lives in useTrainInputForm.

export function TrainInputForm({ form }: { form: TrainInputFormState }) {
  const { trains, trainName, selected, schema, fields } = form;
  return (
    <>
      <label className="block text-xs text-muted mb-1">Train</label>
      <select
        aria-label="Train"
        value={trainName}
        onChange={(e) => form.selectTrain(e.target.value)}
        className="w-full text-sm border border-line-strong rounded-md px-2 py-2 mb-4 bg-field"
      >
        <option value="">Select a train…</option>
        {/* A preselected train not in the (admin-hidden) list still shows as chosen. */}
        {trainName && !trains.some((t) => t.fullName === trainName) && (
          <option value={trainName}>{shortName(trainName)}</option>
        )}
        {trains.map((t) => (
          <option key={t.fullName} value={t.fullName}>
            {shortName(t.fullName)}
          </option>
        ))}
      </select>

      {schema.length > 0 && (
        <div className="mb-4">
          <label className="block text-xs text-muted mb-1">
            Input ({shortName(selected!.inputTypeName)})
          </label>
          <div className="space-y-2">
            {schema.map((p) => (
              <div key={p.name} className="flex items-center gap-2">
                <span
                  className="text-xs text-fg-2 w-40 shrink-0 truncate"
                  title={`${p.name}: ${p.typeName}`}
                >
                  {p.name}
                </span>
                {isEnumProperty(p) ? (
                  <select
                    aria-label={`Field ${p.name}`}
                    value={fields[p.name] ?? ""}
                    onChange={(e) => form.setField(p.name, e.target.value)}
                    className="flex-1 text-sm border border-line-strong rounded-md px-2 py-1 bg-field"
                  >
                    <option value="">{p.isNullable ? "(none)" : "(default)"}</option>
                    {p.enumValues!.map((v) => (
                      <option key={v} value={v}>
                        {v}
                      </option>
                    ))}
                  </select>
                ) : isBooleanProperty(p) ? (
                  <input
                    type="checkbox"
                    aria-label={`Field ${p.name}`}
                    checked={fields[p.name] === "true"}
                    onChange={(e) => form.setField(p.name, e.target.checked ? "true" : "")}
                  />
                ) : (
                  <input
                    aria-label={`Field ${p.name}`}
                    value={fields[p.name] ?? ""}
                    onChange={(e) => form.setField(p.name, e.target.value)}
                    placeholder={p.typeName + (p.isNullable ? "?" : "")}
                    className="flex-1 text-sm border border-line-strong rounded-md px-2 py-1 bg-field"
                  />
                )}
              </div>
            ))}
          </div>
        </div>
      )}

      <label className="block text-xs text-muted mb-1">
        Input JSON (overrides the fields above; leave blank for Unit)
      </label>
      <textarea
        aria-label="Input JSON"
        value={form.inputJson}
        onChange={(e) => form.setInputJson(e.target.value)}
        rows={6}
        placeholder='{ "playerId": "player-42" }'
        className="w-full text-sm font-mono border border-line-strong rounded-md px-2 py-2 mb-4 bg-field"
      />
    </>
  );
}
