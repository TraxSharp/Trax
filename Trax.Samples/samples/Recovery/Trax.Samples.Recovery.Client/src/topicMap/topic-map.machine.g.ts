// AUTO-GENERATED from topic-map.ir.json by generateMachineFactory. Do not edit by hand.
// Imports the engine from "@traxsharp/state-machine" (trax machine generate --import-style specifier, the
// default without --engine-src). Name another module with --specifier <module>, or import an engine
// checkout by relative path with --import-style relative --engine-src <engine/src>.

import { typedMachineFromIr, type IrDocument } from "@traxsharp/state-machine";
import type { TopicMapSpec } from "./topic-map.contexts.g";

const ir = {
  "irFormat": 1,
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
          "type": "array",
          "values": {
            "nullable": false,
            "type": "string"
          }
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "fromYear",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "name": "mapId",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "papers",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "toYear",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "topicPairs",
          "nullable": true,
          "type": "integer"
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
          "type": "array",
          "values": {
            "nullable": false,
            "type": "string"
          }
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "fromYear",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "name": "mapId",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "papers",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "toYear",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "topicPairs",
          "nullable": true,
          "type": "integer"
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
          "type": "array",
          "values": {
            "nullable": false,
            "type": "string"
          }
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "fromYear",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "name": "mapId",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "papers",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "toYear",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "topicPairs",
          "nullable": true,
          "type": "integer"
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
          "type": "array",
          "values": {
            "nullable": false,
            "type": "string"
          }
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "fromYear",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "name": "mapId",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "papers",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "toYear",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "topicPairs",
          "nullable": true,
          "type": "integer"
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
          "type": "array",
          "values": {
            "nullable": false,
            "type": "string"
          }
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "fromYear",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "name": "mapId",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "papers",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "toYear",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "topicPairs",
          "nullable": true,
          "type": "integer"
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
          "type": "array",
          "values": {
            "nullable": false,
            "type": "string"
          }
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "fromYear",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "name": "mapId",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "papers",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "toYear",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "topicPairs",
          "nullable": true,
          "type": "integer"
        }
      ]
    },
    "Expired": {
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
          "type": "array",
          "values": {
            "nullable": false,
            "type": "string"
          }
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "fromYear",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "name": "mapId",
          "nullable": true,
          "type": "string"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "papers",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "toYear",
          "nullable": true,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "topicPairs",
          "nullable": true,
          "type": "integer"
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
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "fromYear",
          "nullable": false,
          "type": "integer"
        },
        {
          "constraints": [],
          "maximum": 2147483647,
          "minimum": -2147483648,
          "name": "toYear",
          "nullable": false,
          "type": "integer"
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
              "source": "input"
            },
            {
              "field": "fields",
              "rule": "arrayOf",
              "source": "input",
              "type": "string"
            }
          ],
          "name": "fields",
          "nullable": false,
          "type": "array",
          "values": {
            "nullable": false,
            "type": "string"
          }
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
    },
    "Expired": {
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
      "state": "Building"
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
      "output": {
        "fields": [
          {
            "constraints": [],
            "name": "coCitationTrack",
            "nullable": false,
            "type": "string"
          },
          {
            "constraints": [],
            "fields": [
              {
                "constraints": [],
                "name": "embedding",
                "nullable": false,
                "type": "number"
              },
              {
                "constraints": [],
                "name": "score",
                "nullable": false,
                "type": "number"
              },
              {
                "constraints": [],
                "maximum": 2147483647,
                "minimum": -2147483648,
                "name": "sharedAuthors",
                "nullable": false,
                "type": "integer"
              },
              {
                "constraints": [],
                "maximum": 2147483647,
                "minimum": -2147483648,
                "name": "sharedReferences",
                "nullable": false,
                "type": "integer"
              },
              {
                "constraints": [],
                "name": "titleA",
                "nullable": false,
                "type": "string"
              },
              {
                "constraints": [],
                "name": "titleB",
                "nullable": false,
                "type": "string"
              },
              {
                "constraints": [],
                "name": "workA",
                "nullable": false,
                "type": "string"
              },
              {
                "constraints": [],
                "name": "workB",
                "nullable": false,
                "type": "string"
              }
            ],
            "name": "hiddenTwins",
            "nullable": false,
            "type": "array"
          },
          {
            "constraints": [],
            "maximum": 2147483647,
            "minimum": -2147483648,
            "name": "papers",
            "nullable": false,
            "type": "integer"
          },
          {
            "constraints": [],
            "name": "runId",
            "nullable": false,
            "type": "string"
          },
          {
            "constraints": [],
            "fields": [
              {
                "constraints": [],
                "name": "embedding",
                "nullable": false,
                "type": "number"
              },
              {
                "constraints": [],
                "name": "score",
                "nullable": false,
                "type": "number"
              },
              {
                "constraints": [],
                "maximum": 2147483647,
                "minimum": -2147483648,
                "name": "sharedAuthors",
                "nullable": false,
                "type": "integer"
              },
              {
                "constraints": [],
                "maximum": 2147483647,
                "minimum": -2147483648,
                "name": "sharedReferences",
                "nullable": false,
                "type": "integer"
              },
              {
                "constraints": [],
                "name": "titleA",
                "nullable": false,
                "type": "string"
              },
              {
                "constraints": [],
                "name": "titleB",
                "nullable": false,
                "type": "string"
              },
              {
                "constraints": [],
                "name": "workA",
                "nullable": false,
                "type": "string"
              },
              {
                "constraints": [],
                "name": "workB",
                "nullable": false,
                "type": "string"
              }
            ],
            "name": "strongest",
            "nullable": false,
            "type": "array"
          },
          {
            "constraints": [],
            "maximum": 2147483647,
            "minimum": -2147483648,
            "name": "topicPairs",
            "nullable": false,
            "type": "integer"
          }
        ]
      },
      "state": "Building"
    },
    "Building.failed": {
      "edges": [
        {
          "to": "BuildFailed"
        }
      ],
      "outcome": "failed",
      "state": "Building"
    }
  },
  "states": [
    "BuildCancelled",
    "BuildFailed",
    "Building",
    "Built",
    "ChoosingFields",
    "ChoosingRange",
    "Expired"
  ],
  "timers": [
    {
      "after": "P7D",
      "rules": [
        "Requires(Operator)"
      ],
      "state": "Built",
      "target": "Expired",
      "trigger": "Built.after"
    }
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
      "from": "BuildCancelled",
      "reduce": {
        "reduce": "reset"
      },
      "to": "ChoosingFields",
      "trigger": "StartOver"
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
      "from": "BuildFailed",
      "reduce": {
        "reduce": "reset"
      },
      "to": "ChoosingFields",
      "trigger": "StartOver"
    },
    {
      "from": "Building",
      "to": "ChoosingRange",
      "trigger": "CancelBuild"
    },
    {
      "from": "Building",
      "reduce": {
        "reduce": "reset"
      },
      "to": "ChoosingFields",
      "trigger": "StartOver"
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
      "from": "Built",
      "reduce": {
        "reduce": "reset"
      },
      "to": "ChoosingFields",
      "trigger": "StartOver"
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
    },
    {
      "from": "Expired",
      "to": "ChoosingRange",
      "trigger": "Edit"
    },
    {
      "from": "Expired",
      "to": "Building",
      "trigger": "Rebuild"
    },
    {
      "from": "Expired",
      "reduce": {
        "reduce": "reset"
      },
      "to": "ChoosingFields",
      "trigger": "StartOver"
    }
  ],
  "triggers": [
    "Back",
    "Build",
    "CancelBuild",
    "ChooseFields",
    "Edit",
    "Rebuild",
    "StartOver"
  ],
  "version": 1
} as IrDocument;

/** SHA-256 of this machine's IR — the version-skew handshake token (matches C#'s IMachine.SchemaHash). */
export const irHash = "acfcf8e48098e5a240701ae073b326d227fdb555d38d66b9a0a9afbadafd7893";

/** The topic-map machine, built from the IR and typed by TopicMapSpec. No hand-written twin. */
export const topicMap = typedMachineFromIr<TopicMapSpec>(ir, irHash);
