import { useState } from "react";
import { buildInputJson } from "./trainInput";
import { useTraxQuery } from "./consoleClient";
import { trainsDocument } from "../trax/index.g";

/** State for the TrainInputForm the Queue and Run dialogs render, with the registered trains. */
export function useTrainInputForm(initialTrain?: string) {
  const [{ data }] = useTraxQuery(trainsDocument, { variables: { hideAdmin: true } });
  const trains = data?.operations?.trains ?? [];
  const [trainName, setTrainName] = useState(initialTrain ?? "");
  const [inputJson, setInputJson] = useState("");
  const [fields, setFields] = useState<Record<string, string>>({});
  const selected = trains.find((t) => t.fullName === trainName);
  const schema = selected?.inputSchema ?? [];

  return {
    trains,
    trainName,
    selected,
    schema,
    fields,
    inputJson,
    selectTrain(name: string) {
      setTrainName(name);
      setFields({});
    },
    setField(name: string, value: string) {
      setFields((f) => ({ ...f, [name]: value }));
    },
    setInputJson,
    /** The JSON to send: the raw box when filled, else the fields; null for none. */
    build(): string | null {
      return inputJson.trim() || buildInputJson(schema, fields) || null;
    },
  };
}

export type TrainInputFormState = ReturnType<typeof useTrainInputForm>;

