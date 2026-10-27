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

The `cannot-reproduce` scenario (`npm run scenario -- cannot-reproduce`). repro-bot sends cases on its assignment;
the state each enters runs `ReproduceTrain`, which checks the case against the shop's rules and policy. A close is
refused until the server has run two cases that did not show the bug:

```text
        2 repro-bot tries the reported order, $75.00. The rules ship it free, as the policy says. ...
 2 send    repro-bot  repro: $75.00 order. policy: ships free.
 2 check   repro-bot  ReproduceTrain failed: $75.00 pays $0.00, which matches the policy: not reproduced
 3 send    repro-bot  close: cannot reproduce.
 3 answer  repro-bot  GUARD_FAILED: Close needs at least two cases the server ran that did not reproduce it.
 4 send    repro-bot  repro: $50.00 order. policy: ships free.
 4 check   repro-bot  ReproduceTrain failed: $50.00 pays $0.00, which matches the policy: not reproduced
 5 send    repro-bot  close: cannot reproduce.
 5 check   repro-bot  CloseTrain done: closed: 2 cases matched the policy
 5 ticket  repro-bot  T-19 Closed: cannot reproduce: 2 cases matched the policy
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
