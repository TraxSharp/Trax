using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Cli.Tests.Fakes;

/// <summary>
/// A client manifest as a host exports one: the checkout machine (an effect) and the reminder machine (a timer) with
/// their IRs and documents, and one train, startRun, with its document and the types it reaches.
/// </summary>
internal static class ClientManifests
{
    public const string TrainTypes = """
        type DispatchMutations {
          startRun(input: StartRunInput!, mode: ExecutionMode = RUN, priority: Int): StartRunResponse!
        }

        enum ExecutionMode {
          RUN
          QUEUE
        }

        "Starts a run. */ alert('x') /*"
        input StartRunInput {
          "The scenario to run."
          scenario: String!
          crashOnce: Boolean
        }

        type StartRunOutput {
          runId: String!
          steps: [String!]!
        }

        type StartRunResponse {
          externalId: String!
          metadataId: Long
          workQueueId: Long
          output: StartRunOutput
        }
        """;

    public const string StartRunDocument =
        "mutation StartRun($input: StartRunInput!, $mode: ExecutionMode, $priority: Int) { dispatch { "
        + "startRun(input: $input, mode: $mode, priority: $priority) { externalId metadataId workQueueId "
        + "output { runId steps } } } }";

    public static ClientManifest Sample(params IMachine[] machines) =>
        new()
        {
            Machines = (
                machines.Length == 0
                    ? [new DeclarativeCheckoutMachine(), new DeclarativeReminderMachine()]
                    : machines
            )
                .Select(Machine)
                .ToList(),
            Trains =
            [
                new ClientManifestTrain
                {
                    Field = "startRun",
                    Root = "mutation",
                    Requires = new ClientManifestRequirement
                    {
                        RoleSets =
                        [
                            ["recovery-user"],
                        ],
                    },
                    Documents =
                    [
                        new ClientManifestDocument
                        {
                            Id =
                                "trax_train_mutation__startRun_"
                                + new string('c', 64)
                                + new string('d', 16),
                            Operation = "startRun",
                            Document = StartRunDocument,
                        },
                    ],
                },
            ],
            Types = TrainTypes,
        };

    public static ClientManifestMachine Machine(IMachine machine)
    {
        var hash = machine.SchemaHash!;
        var field = Trax.Cli.Clients.TypeScript.Camel(machine.Name);
        return new ClientManifestMachine
        {
            Id = machine.Name,
            IrHash = hash,
            Ir = (JsonObject)JsonNode.Parse(machine.ExportIr())!,
            Documents =
            [
                Document(
                    machine.Name,
                    "get",
                    hash,
                    $"query G($id: UUID!) {{ machines {{ {field} {{ get(id: $id) {{ id }} }} }} }}"
                ),
                Document(
                    machine.Name,
                    "start",
                    hash,
                    $"mutation S($requestId: String!) {{ machines {{ {field} {{ start(requestId: $requestId) {{ id }} }} }} }}"
                ),
                Document(
                    machine.Name,
                    "changed",
                    hash,
                    $"subscription C($id: UUID!) {{ {field}Changed(id: $id) {{ revision }} }}"
                ),
            ],
        };
    }

    private static ClientManifestDocument Document(
        string machine,
        string operation,
        string hash,
        string text
    ) =>
        new()
        {
            Id = $"trax_machine_{machine}_{operation}_{hash}{new string('e', 16)}",
            Operation = operation,
            Document = text,
        };
}
