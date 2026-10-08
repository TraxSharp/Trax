// AUTO-GENERATED from topic-map.ir.json by generateContextTypes. Do not edit by hand.

export type TopicMapState = "BuildCancelled" | "BuildFailed" | "Building" | "Built" | "ChoosingFields" | "ChoosingRange";
export type TopicMapTrigger = "Back" | "Build" | "CancelBuild" | "ChooseFields" | "Edit" | "Rebuild";
export type TopicMapOutcome = "Building.cancelled" | "Building.done" | "Building.failed";

export type TopicMapBuildCancelledContext = {
  coCitationTrack?: string | null;
  fields?: string[] | null;
  fromYear?: number | null;
  mapId?: string | null;
  papers?: number | null;
  toYear?: number | null;
  topicPairs?: number | null;
};

export type TopicMapBuildFailedContext = {
  coCitationTrack?: string | null;
  fields?: string[] | null;
  fromYear?: number | null;
  mapId?: string | null;
  papers?: number | null;
  toYear?: number | null;
  topicPairs?: number | null;
};

export type TopicMapBuildingContext = {
  coCitationTrack?: string | null;
  fields?: string[] | null;
  fromYear?: number | null;
  mapId?: string | null;
  papers?: number | null;
  toYear?: number | null;
  topicPairs?: number | null;
};

export type TopicMapBuiltContext = {
  coCitationTrack?: string | null;
  fields?: string[] | null;
  fromYear?: number | null;
  mapId?: string | null;
  papers?: number | null;
  toYear?: number | null;
  topicPairs?: number | null;
};

export type TopicMapChoosingFieldsContext = {
  coCitationTrack?: string | null;
  fields?: string[] | null;
  fromYear?: number | null;
  mapId?: string | null;
  papers?: number | null;
  toYear?: number | null;
  topicPairs?: number | null;
};

export type TopicMapChoosingRangeContext = {
  coCitationTrack?: string | null;
  fields?: string[] | null;
  fromYear?: number | null;
  mapId?: string | null;
  papers?: number | null;
  toYear?: number | null;
  topicPairs?: number | null;
};

export type TopicMapContexts = {
  BuildCancelled: TopicMapBuildCancelledContext;
  BuildFailed: TopicMapBuildFailedContext;
  Building: TopicMapBuildingContext;
  Built: TopicMapBuiltContext;
  ChoosingFields: TopicMapChoosingFieldsContext;
  ChoosingRange: TopicMapChoosingRangeContext;
};

export type TopicMapBuildInput = {
  fromYear: number;
  toYear: number;
};

export type TopicMapChooseFieldsInput = {
  fields: string[];
};

export type TopicMapBuildingDoneOutput = {
  coCitationTrack: string;
  hiddenTwins: unknown[];
  papers: number;
  runId: string;
  strongest: unknown[];
  topicPairs: number;
};

export type TopicMapInputs = {
  Back: undefined;
  Build: TopicMapBuildInput;
  CancelBuild: undefined;
  ChooseFields: TopicMapChooseFieldsInput;
  Edit: undefined;
  Rebuild: undefined;
  "Building.cancelled": undefined;
  "Building.done": TopicMapBuildingDoneOutput;
  "Building.failed": undefined;
};

export type TopicMapSpec = {
  states: TopicMapContexts;
  triggers: TopicMapInputs;
};
