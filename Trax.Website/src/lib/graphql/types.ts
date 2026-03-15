export interface HealthData {
  operations: {
    health: {
      status: string;
      description: string;
      queueDepth: number;
      inProgress: number;
      failedLastHour: number;
      deadLetters: number;
    };
  };
}

export interface TrainInfo {
  serviceTypeName: string;
  implementationTypeName: string;
  inputTypeName: string;
  outputTypeName: string;
  lifetime: string;
  isQuery: boolean;
  isMutation: boolean;
  graphQLName: string;
}

export interface TrainsData {
  operations: {
    trains: TrainInfo[];
  };
}

export interface ManifestSummary {
  id: string;
  externalId: string;
  name: string;
  isEnabled: boolean;
  scheduleType: string;
  cronExpression: string | null;
  intervalSeconds: number | null;
  maxRetries: number;
  timeoutSeconds: number;
  lastSuccessfulRun: string | null;
  priority: number;
}

export interface ManifestsData {
  operations: {
    manifests: {
      items: ManifestSummary[];
      totalCount: number;
    };
  };
}

export interface TrainEvent {
  metadataId: string;
  externalId: string;
  trainName: string;
  trainState: string;
  timestamp: string;
  failureStep?: string;
  failureReason?: string;
}

export interface TrainStateChangedData {
  onTrainStateChanged: TrainEvent;
}
