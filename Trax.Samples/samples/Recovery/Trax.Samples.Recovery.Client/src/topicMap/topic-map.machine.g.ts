// AUTO-GENERATED from topic-map.ir.json by generateMachineFactory. Do not edit by hand.

import { typedMachineFromIr, type IrDocument } from "@trax/state-machine";
import type { TopicMapSpec } from "./topic-map.contexts.g";

const ir = {
  "committedStates": [],
  "context": {
    "BuildCancelled": {
      "fields": [
        {
          "constraints": [],
          "name": "coCitationTrack",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [
            {
              "field": "fields",
              "rule": "arrayOf",
              "source": "context",
              "type": "string"
            }
          ],
          "name": "fields",
          "nullable": true,
          "type": "array"
        },
        {
          "constraints": [],
          "name": "fromYear",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "mapId",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "papers",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "toYear",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "topicPairs",
          "nullable": true,
          "type": "number"
        }
      ]
    },
    "BuildFailed": {
      "fields": [
        {
          "constraints": [],
          "name": "coCitationTrack",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [
            {
              "field": "fields",
              "rule": "arrayOf",
              "source": "context",
              "type": "string"
            }
          ],
          "name": "fields",
          "nullable": true,
          "type": "array"
        },
        {
          "constraints": [],
          "name": "fromYear",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "mapId",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "papers",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "toYear",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "topicPairs",
          "nullable": true,
          "type": "number"
        }
      ]
    },
    "Building": {
      "fields": [
        {
          "constraints": [],
          "name": "coCitationTrack",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [
            {
              "field": "fields",
              "rule": "arrayOf",
              "source": "context",
              "type": "string"
            }
          ],
          "name": "fields",
          "nullable": true,
          "type": "array"
        },
        {
          "constraints": [],
          "name": "fromYear",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "mapId",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "papers",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "toYear",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "topicPairs",
          "nullable": true,
          "type": "number"
        }
      ]
    },
    "Built": {
      "fields": [
        {
          "constraints": [],
          "name": "coCitationTrack",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [
            {
              "field": "fields",
              "rule": "arrayOf",
              "source": "context",
              "type": "string"
            }
          ],
          "name": "fields",
          "nullable": true,
          "type": "array"
        },
        {
          "constraints": [],
          "name": "fromYear",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "mapId",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "papers",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "toYear",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "topicPairs",
          "nullable": true,
          "type": "number"
        }
      ]
    },
    "ChoosingFields": {
      "fields": [
        {
          "constraints": [],
          "name": "coCitationTrack",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [
            {
              "field": "fields",
              "rule": "arrayOf",
              "source": "context",
              "type": "string"
            }
          ],
          "name": "fields",
          "nullable": true,
          "type": "array"
        },
        {
          "constraints": [],
          "name": "fromYear",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "mapId",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "papers",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "toYear",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "topicPairs",
          "nullable": true,
          "type": "number"
        }
      ]
    },
    "ChoosingRange": {
      "fields": [
        {
          "constraints": [],
          "name": "coCitationTrack",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [
            {
              "field": "fields",
              "rule": "arrayOf",
              "source": "context",
              "type": "string"
            }
          ],
          "name": "fields",
          "nullable": true,
          "type": "array"
        },
        {
          "constraints": [],
          "name": "fromYear",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "mapId",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "papers",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "toYear",
          "nullable": true,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "topicPairs",
          "nullable": true,
          "type": "number"
        }
      ]
    }
  },
  "id": "topic-map",
  "initialContext": {},
  "initialState": "ChoosingFields",
  "inputs": {
    "Build": {
      "fields": [
        {
          "constraints": [],
          "name": "fromYear",
          "nullable": false,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "toYear",
          "nullable": false,
          "type": "number"
        }
      ]
    },
    "Building.done": {
      "fields": [
        {
          "constraints": [],
          "name": "coCitationTrack",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "hiddenTwins",
          "nullable": false,
          "type": "array"
        },
        {
          "constraints": [],
          "name": "papers",
          "nullable": false,
          "type": "number"
        },
        {
          "constraints": [],
          "name": "runId",
          "nullable": false,
          "type": "string"
        },
        {
          "constraints": [],
          "name": "strongest",
          "nullable": false,
          "type": "array"
        },
        {
          "constraints": [],
          "name": "topicPairs",
          "nullable": false,
          "type": "number"
        }
      ]
    },
    "ChooseFields": {
      "fields": [
        {
          "constraints": [
            {
              "field": "fields",
              "rule": "nonEmpty",
              "source": "context"
            },
            {
              "field": "fields",
              "rule": "arrayOf",
              "source": "context",
              "type": "string"
            }
          ],
          "name": "fields",
          "nullable": false,
          "type": "array"
        }
      ]
    }
  },
  "invariants": {
    "BuildCancelled": {
      "rule": "all",
      "rules": [
        {
          "field": "fields",
          "op": "gte",
          "rule": "count",
          "source": "context",
          "value": 1
        },
        {
          "field": "fromYear",
          "rule": "present",
          "source": "context"
        },
        {
          "field": "toYear",
          "rule": "present",
          "source": "context"
        }
      ]
    },
    "BuildFailed": {
      "rule": "all",
      "rules": [
        {
          "field": "fields",
          "op": "gte",
          "rule": "count",
          "source": "context",
          "value": 1
        },
        {
          "field": "fromYear",
          "rule": "present",
          "source": "context"
        },
        {
          "field": "toYear",
          "rule": "present",
          "source": "context"
        }
      ]
    },
    "Building": {
      "rule": "all",
      "rules": [
        {
          "field": "fields",
          "op": "gte",
          "rule": "count",
          "source": "context",
          "value": 1
        },
        {
          "field": "fromYear",
          "rule": "present",
          "source": "context"
        },
        {
          "field": "toYear",
          "rule": "present",
          "source": "context"
        }
      ]
    },
    "Built": {
      "rule": "all",
      "rules": [
        {
          "rule": "all",
          "rules": [
            {
              "field": "fields",
              "op": "gte",
              "rule": "count",
              "source": "context",
              "value": 1
            },
            {
              "field": "fromYear",
              "rule": "present",
              "source": "context"
            },
            {
              "field": "toYear",
              "rule": "present",
              "source": "context"
            }
          ]
        },
        {
          "field": "mapId",
          "rule": "nonEmpty",
          "source": "context"
        }
      ]
    },
    "ChoosingRange": {
      "field": "fields",
      "op": "gte",
      "rule": "count",
      "source": "context",
      "value": 1
    }
  },
  "outcomes": {
    "Building.cancelled": {
      "edges": [
        {
          "to": "BuildCancelled"
        }
      ],
      "outcome": "cancelled",
      "state": "Building",
      "train": "Trax.Samples.Recovery.Trains.Topics.IBuildTopicMapTrain"
    },
    "Building.done": {
      "edges": [
        {
          "reduce": {
            "reduce": "set",
            "steps": [
              {
                "field": "mapId",
                "value": {
                  "input": "runId"
                }
              },
              {
                "field": "papers",
                "value": {
                  "input": "papers"
                }
              },
              {
                "field": "topicPairs",
                "value": {
                  "input": "topicPairs"
                }
              },
              {
                "field": "coCitationTrack",
                "value": {
                  "input": "coCitationTrack"
                }
              }
            ]
          },
          "to": "Built"
        }
      ],
      "outcome": "done",
      "state": "Building",
      "train": "Trax.Samples.Recovery.Trains.Topics.IBuildTopicMapTrain"
    },
    "Building.failed": {
      "edges": [
        {
          "to": "BuildFailed"
        }
      ],
      "outcome": "failed",
      "state": "Building",
      "train": "Trax.Samples.Recovery.Trains.Topics.IBuildTopicMapTrain"
    }
  },
  "states": [
    "BuildCancelled",
    "BuildFailed",
    "Building",
    "Built",
    "ChoosingFields",
    "ChoosingRange"
  ],
  "transitions": [
    {
      "from": "BuildCancelled",
      "to": "ChoosingRange",
      "trigger": "Edit"
    },
    {
      "from": "BuildCancelled",
      "to": "Building",
      "trigger": "Rebuild"
    },
    {
      "from": "BuildFailed",
      "to": "ChoosingRange",
      "trigger": "Edit"
    },
    {
      "from": "BuildFailed",
      "to": "Building",
      "trigger": "Rebuild"
    },
    {
      "from": "Building",
      "to": "ChoosingRange",
      "trigger": "CancelBuild"
    },
    {
      "from": "Built",
      "to": "ChoosingRange",
      "trigger": "Edit"
    },
    {
      "from": "Built",
      "to": "Building",
      "trigger": "Rebuild"
    },
    {
      "from": "ChoosingFields",
      "guard": {
        "field": "fields",
        "op": "gte",
        "rule": "count",
        "source": "input",
        "value": 1
      },
      "guardMessage": "Choose at least one field.",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "fields",
            "value": {
              "input": "fields"
            }
          }
        ]
      },
      "to": "ChoosingRange",
      "trigger": "ChooseFields"
    },
    {
      "from": "ChoosingRange",
      "to": "ChoosingFields",
      "trigger": "Back"
    },
    {
      "from": "ChoosingRange",
      "guard": {
        "rule": "all",
        "rules": [
          {
            "field": "fromYear",
            "rule": "ofType",
            "source": "input",
            "type": "number"
          },
          {
            "field": "toYear",
            "rule": "ofType",
            "source": "input",
            "type": "number"
          }
        ]
      },
      "guardMessage": "Choose the first and the last year.",
      "reduce": {
        "reduce": "set",
        "steps": [
          {
            "field": "fromYear",
            "value": {
              "input": "fromYear"
            }
          },
          {
            "field": "toYear",
            "value": {
              "input": "toYear"
            }
          }
        ]
      },
      "to": "Building",
      "trigger": "Build"
    }
  ],
  "triggers": [
    "Back",
    "Build",
    "CancelBuild",
    "ChooseFields",
    "Edit",
    "Rebuild"
  ],
  "version": 1
} as IrDocument;

/** SHA-256 of this machine's IR — the version-skew handshake token (matches C#'s IMachine.SchemaHash). */
export const irHash = "96869600c6682ca3f57e89f2e777a6fcafe3dfdc5654c3530ad0fc116dd625ec";

/** The topic-map machine, built from the IR and typed by TopicMapSpec. No hand-written twin. */
export const topicMap = typedMachineFromIr<TopicMapSpec>(ir, irHash);
