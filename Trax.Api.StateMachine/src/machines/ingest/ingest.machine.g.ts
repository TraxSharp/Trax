// AUTO-GENERATED from ingest.ir.json by generateMachineFactory. Do not edit by hand.

import { typedMachineFromIr } from "../../typed";
import type { IrDocument } from "../../rules/irMachine";
import type { IngestSpec } from "./ingest.contexts.g";

const ir = {
  "committedStates": [],
  "context": {
    "Approved": {
      "fields": [
        {
          "constraints": [
            {
              "field": "fingerprint",
              "rule": "nonEmpty",
              "source": "context"
            }
          ],
          "name": "fingerprint",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [
            {
              "field": "source",
              "rule": "nonEmpty",
              "source": "context"
            }
          ],
          "name": "source",
          "nullable": false,
          "type": "string"
        }
      ]
    },
    "Cancelled": {
      "fields": [
        {
          "constraints": [
            {
              "field": "source",
              "rule": "nonEmpty",
              "source": "context"
            }
          ],
          "name": "source",
          "nullable": false,
          "type": "string"
        }
      ]
    },
    "FetchFailed": {
      "fields": [
        {
          "constraints": [
            {
              "field": "source",
              "rule": "nonEmpty",
              "source": "context"
            }
          ],
          "name": "source",
          "nullable": false,
          "type": "string"
        }
      ]
    },
    "Fetched": {
      "fields": [
        {
          "constraints": [
            {
              "field": "fingerprint",
              "rule": "nonEmpty",
              "source": "context"
            }
          ],
          "name": "fingerprint",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [
            {
              "field": "source",
              "rule": "nonEmpty",
              "source": "context"
            }
          ],
          "name": "source",
          "nullable": false,
          "type": "string"
        }
      ]
    },
    "Fetching": {
      "fields": [
        {
          "constraints": [
            {
              "field": "source",
              "rule": "nonEmpty",
              "source": "context"
            }
          ],
          "name": "source",
          "nullable": false,
          "type": "string"
        }
      ]
    },
    "Idle": {
      "fields": [
        {
          "constraints": [
            {
              "field": "source",
              "rule": "nonEmpty",
              "source": "context"
            }
          ],
          "name": "source",
          "nullable": false,
          "type": "string"
        }
      ]
    },
    "NeedsReview": {
      "fields": [
        {
          "constraints": [
            {
              "field": "fingerprint",
              "rule": "nonEmpty",
              "source": "context"
            }
          ],
          "name": "fingerprint",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [
            {
              "field": "source",
              "rule": "nonEmpty",
              "source": "context"
            }
          ],
          "name": "source",
          "nullable": false,
          "type": "string"
        }
      ]
    }
  },
  "id": "ingest",
  "initialContext": {
    "source": "s3://bucket/partition-1"
  },
  "initialState": "Idle",
  "inputs": {
    "Fetching.done": {
      "fields": [
        {
          "constraints": [],
          "name": "fingerprint",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "unsure",
          "nullable": false,
          "type": "boolean"
        }
      ]
    }
  },
  "outcomes": {
    "Fetching.cancelled": {
      "edges": [
        {
          "to": "Cancelled"
        }
      ],
      "outcome": "cancelled",
      "state": "Fetching",
      "train": "Ingest.Contracts.IFetchTrain"
    },
    "Fetching.done": {
      "edges": [
        {
          "guard": {
            "field": "unsure",
            "rule": "boolEquals",
            "source": "input",
            "value": true
          },
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "fingerprint",
                "value": {
                  "input": "fingerprint"
                }
              }
            ]
          },
          "to": "NeedsReview"
        },
        {
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "fingerprint",
                "value": {
                  "input": "fingerprint"
                }
              }
            ]
          },
          "to": "Fetched"
        }
      ],
      "outcome": "done",
      "state": "Fetching",
      "train": "Ingest.Contracts.IFetchTrain"
    },
    "Fetching.failed": {
      "edges": [
        {
          "to": "FetchFailed"
        }
      ],
      "outcome": "failed",
      "state": "Fetching",
      "train": "Ingest.Contracts.IFetchTrain"
    }
  },
  "states": [
    "Approved",
    "Cancelled",
    "FetchFailed",
    "Fetched",
    "Fetching",
    "Idle",
    "NeedsReview"
  ],
  "transitions": [
    {
      "from": "Cancelled",
      "to": "Fetching",
      "trigger": "Retry"
    },
    {
      "from": "FetchFailed",
      "to": "Fetching",
      "trigger": "Retry"
    },
    {
      "from": "Fetching",
      "to": "Idle",
      "trigger": "Abandon"
    },
    {
      "from": "Idle",
      "to": "Fetching",
      "trigger": "Start"
    },
    {
      "from": "NeedsReview",
      "to": "Approved",
      "trigger": "Approve"
    }
  ],
  "triggers": [
    "Abandon",
    "Approve",
    "Retry",
    "Start"
  ],
  "version": 1
} as IrDocument;

/** SHA-256 of this machine's IR — the version-skew handshake token (matches C#'s IMachine.SchemaHash). */
export const irHash = "33f0b1ab8aa22acae856477e43887d4235d0bfc8785140ca6e2539f7d610b79c";

/** The ingest machine, built from the IR and typed by IngestSpec. No hand-written twin. */
export const ingest = typedMachineFromIr<IngestSpec>(ir, irHash);
