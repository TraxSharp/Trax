---
title: Board
description: "Reference for the operations console's Board page: each machine served from features drawn as an event-storming board, and the scenarios behind each note."
type: reference
area: dashboard
nav_order: 3
source:
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/pages/BoardPage.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/components/MachineBoard.tsx
  - Trax.Api/src/Trax.Api.Dashboard/ClientApp/src/lib/machineBoard.ts
---

# Board

`/board` and `/board/{machine}`. A [machine written as features](/docs/statemachine/write-machines-as-features),
drawn as the event-storming board the team would put on a wall, from
[`operations.machineBoard`](/docs/sdk-reference/graphql-api/queries/machine-instances). It is the
same story the features tell, so it reads without knowing the code, and every note says which
scenarios put it there.

The left of the page lists every machine the host registers. A machine that is not served from
features has no board and is marked **no board**. With no machine in the URL, the page opens the
first one that has a board.

## The wall

One lane for the machine, and one more for each machine its rules reach, read left to right in the
order the machine's states are reached. Each note is the colour event storming gives its role, and
the role is written on it:

| Note | Colour | What it is |
|---|---|---|
| **Machine** | Yellow, first in its lane | The machine itself, with whoever starts it above |
| **Actor** | Small, pale yellow | Who sends the command below it, or reads the view below it |
| **Command** | Blue | What an actor or a rule asks the machine to do |
| **Event** | Orange | What happened: a move from one state to another |
| **Policy** | Lilac | A rule: a guard on the command below it, a timer, or "when that machine gets here, do this" |
| **View** | Green, last in its lane | What people read, and who reads it |

Each column is one command: the actors who send it, the rules that send or guard it, the command,
and the moves it makes. A timer or a run's outcome that moves the machine with no command has a
column of its own. A rule that starts a command in another machine draws an arrow from the event that
sets it off to the rule, in the other machine's lane.

Arrows run down each column. An arrow to another column or lane would cross the notes between, so it
is drawn only once a note at either end is clicked. Clicking a note lights up the notes it is linked
to and lists the scenarios it came from, by file, line and name. A host file written without
provenance draws the board and says it names no scenarios. Step text never reaches the console.

The machine's states and instances are on [its page](/docs/dashboard/machines#a-machine), linked
from the board.
