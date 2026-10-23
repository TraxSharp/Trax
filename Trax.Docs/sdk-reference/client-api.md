---
title: Client API
description: "Index of the Trax.Client reference: sending a Trax host's persisted GraphQL documents from .NET over HTTP and a graphql-transport-ws socket."
type: landing
area: api
parent: SDK Reference
has_children: true
nav_order: 12.5
---

# Client API

Reference for `Trax.Client`, the .NET runtime that sends a Trax host's GraphQL documents: a query or mutation over
HTTP POST, a subscription over one graphql-transport-ws socket, each by the persisted id the host registered it under
or as text. It references only the base class library, so a terminal tool, a MAUI app or a Blazor WebAssembly app can
take it without the state-machine engine.

## Trax.Client

- [.NET client](/docs/sdk-reference/client-api/dotnet-client): `TraxClient`, `TraxDocument`, `TraxHttpTransport`,
  `GraphQLSubscriptionSocket`, `Optional<T>` and the answers every request comes back as.

`Trax.StateMachine.Client` is built on it: its [.NET client](/docs/sdk-reference/statemachine-api/dotnet-client) drives
a machine a host serves, and posts and subscribes through this package.
