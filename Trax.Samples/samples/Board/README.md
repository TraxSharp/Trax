# Board

A project board for bug tickets, worked by clients the server does not trust. Four scripted agents and one person
each have a key and a lane. An agent claims a ticket, sends its evidence on an assignment of its own, and the server
runs that evidence itself before the ticket moves. The full walk-through is at
[traxsharp.net/docs/samples/board](https://traxsharp.net/docs/samples/board).

## Run

From `Trax.Samples/`:

```bash
docker compose up -d database
dotnet run --project samples/Board/Trax.Samples.Board.Api       # http://localhost:5290
(cd samples/Board/Trax.Samples.Board.Agents && npm ci && npm run scenario)
(cd samples/Board/Trax.Samples.Board.Client && npm ci && npm run dev)   # the board, http://localhost:5173
```

## Example

repro-bot sends a case on its assignment. The state the event enters runs `ReproduceTrain`, which checks the case
against the shop's rules and policy and moves the ticket only if it shows the bug:

```text
 4 claim   repro-bot  claim: reproduce.
 5 send    repro-bot  case 1 of n: below threshold.
 5 check   repro-bot  ReproduceTrain failed: the rules charge $5.99 at $49.99, which is right: not reproduced
 6 send    repro-bot  case 2 of n: at threshold.
 6 check   repro-bot  ReproduceTrain done: the rules charge $5.99 at $50.00; the policy says $0.00
 6 ticket  repro-bot  T-12 ReadyToFix: repro: $50.00 pays $5.99
```

After changing a machine, a train or a document, write both generated clients again (the agents' and the page's)
with the commands on the docs page. CI checks them against the host.

## Where this fits

`Trax.Samples.Board` holds the two machines (`TicketMachine`, system-owned, and `AssignmentMachine`, served) and the
trains; `Trax.Samples.Board.Api` is the host; `Trax.Samples.Board.Agents` and `Trax.Samples.Board.Client` are the
agents and the page, each on its own generated client. The end-to-end suite is `tests/Trax.Samples.Board.E2E`, and
`scripts/recordings/board.mjs` records the agents' runs for traxsharp.net.

## Contributing

See [traxsharp.net/docs/contributing](https://traxsharp.net/docs/contributing).

## Licence

MIT.
