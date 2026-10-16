# @traxsharp/state-machine

[![npm](https://img.shields.io/npm/v/@traxsharp/state-machine)](https://www.npmjs.com/package/@traxsharp/state-machine)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://github.com/TraxSharp/Trax/blob/main/LICENSE)
[![Docs](https://img.shields.io/badge/docs-traxsharp.net-blue)](https://traxsharp.net/docs/statemachine)

> Part of [Trax .NET](https://github.com/TraxSharp): business logic you can call, schedule, or serve as an API, with every
> run recorded in your Postgres. [Docs](https://traxsharp.net/docs) · [Getting started](https://traxsharp.net/docs/getting-started) · [Repository](https://github.com/TraxSharp/Trax)

The TypeScript twin of `Trax.Effect.StateMachine`. A machine is declared once in C#; the twin generated from it runs
the same rules in the browser, so a page can show what an event will do before the server, which applies every
transition, answers. It also holds the client a page uses to drive a machine a Trax host serves, and its React hooks.

## Install

```bash
npm install @traxsharp/state-machine graphql-ws
```

`graphql-ws` and React are optional peer dependencies: the socket a machine's pushes arrive on, and the hooks in
`@traxsharp/state-machine/react`. The package is released with Trax, at the same version as the `Trax.*` NuGet
packages.

## Example

`trax client` generates a typed client for every machine a host exposes. A page configures it once and uses the
machine's hook:

```tsx
import { configureTrax, useRememberedCheckout } from "./trax/index.g";

configureTrax({
  url: "https://shop.example/trax/graphql",
  socketUrl: "wss://shop.example/trax/graphql",
  headers: () => ({ "X-Api-Key": key }),
  connectionParams: () => ({ apiKey: key }),
  user: userId,
});

function PayButton() {
  const checkout = useRememberedCheckout();
  if (checkout.error) return <p>{checkout.error.code}</p>;
  return (
    <button disabled={!checkout.can("Pay")} onClick={() => checkout.send("Pay")}>
      Pay ({checkout.state})
    </button>
  );
}
```

`useRememberedCheckout` opens the instance this browser holds for the user, or starts one. `send` shows the twin's
prediction at once and replaces it with what the server stored. The same machine can be driven without React
(`ServerMachineController`), in memory with no server (`MachineController`), or from a form that owns its own data
(`createFormView`).

## Where this fits

- [State machines](https://traxsharp.net/docs/statemachine): declaring a machine in C#, generating its twin, and
  serving it.
- [Drive a machine instance from React](https://traxsharp.net/docs/statemachine/drive-a-machine-instance-from-react)
  and [Call a machine from TypeScript](https://traxsharp.net/docs/statemachine/call-a-machine-from-typescript).
- [TypeScript package reference](https://traxsharp.net/docs/sdk-reference/statemachine-api/typescript-package): every
  export, by entry point.
- [`trax client`](https://traxsharp.net/docs/reference/cli#frontend-clients-trax-client): the client generated from a
  host's manifest.

## Contributing

Read [AGENTS.md](https://github.com/TraxSharp/Trax/blob/main/Trax.Api.StateMachine/AGENTS.md) before changing code.
Report vulnerabilities privately as described in [SECURITY.md](https://github.com/TraxSharp/Trax/blob/main/SECURITY.md).

## License

MIT. There is no commercial edition, and there will not be one.

Trax .NET is an independent open-source project and is not affiliated with the Utah Transit Authority, Trax Retail, or any
other organization using the Trax name.
