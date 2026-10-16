---
title: Structuring your application
description: "How the project templates lay out a Trax application, where trains, data and tests go, and when to move trains into a class library."
type: explanation
area: general
nav_order: 3
---

# Structuring your application

The [project templates](/docs/reference/templates) lay a Trax server out like this, and nothing
in Trax depends on the folder names, so treat it as a convention rather than a requirement:

```
MyCompany.Hub/
├── MyCompany.Hub.csproj        the web host
├── Directory.Packages.props    every package version (Central Package Management)
├── Program.cs                  every registration, in one file
├── Auth/                       credentials (the Development-only demo key)
├── Data/                       your own EF Core context, its interface and its models
├── Trains/
│   └── <TrainName>/
│       ├── I<TrainName>Train.cs     the interface the train is registered and recorded under
│       ├── <TrainName>Train.cs      the train: attributes and Junctions(), nothing else
│       ├── <TrainName>Input.cs      its input (and Output, when it returns one)
│       ├── Junctions/
│       │   └── <Step>Junction.cs    one class per step
│       └── Models/                  every other type the train uses, internal unless the
│                                    input or output is made of it
└── tests/
    └── MyCompany.Hub.Tests/    UnitTests/ for junctions, IntegrationTests/ for trains and the host
```

`AddMediator(typeof(Program).Assembly)` registers every train in the assembly, wherever it sits,
so adding a train is adding its folder. A train in an assembly `AddMediator` does not scan is not
registered.

## Trains in a class library

When the trains outgrow the host, or a second process needs them, they move into a class library
and each process becomes a thin executable that references it. The library holds what the
application does: the trains and their junctions, the `I<Name>Train` interfaces, the inputs and
outputs, the constants manifests are scheduled under, and any shared models. It has no
`Program.cs` and no `appsettings.json`. The executables decide how it runs: one schedules, one
serves GraphQL, one only polls for jobs, or one process does all three. Every process that runs
the trains points at the same database.

Not every sample is split this way. Auth and SignalRBroadcaster are single hosts with their trains
inside them, which is the template layout above. The [Energy Hub](/docs/samples/energy-hub) sample
is the split at its plainest: a library, a hub and a worker.

```
Trax.Samples.EnergyHub/            class library: trains, ManifestNames, roles, event names
  Trains/<Area>/<Train>/           I<Train>Train.cs, <Train>Train.cs, input, output, Junctions/
  ManifestNames.cs
Trax.Samples.EnergyHub.Hub/        executable: scheduler, GraphQL, dashboard
  Program.cs
Trax.Samples.EnergyHub.Worker/     executable: runs the queued jobs
  Program.cs
```

The library is a plain `Microsoft.NET.Sdk` project with the ASP.NET Core framework reference and
the Trax packages its trains compile against:

```xml from=Trax.Samples/samples/DistributedWorkers/Trax.Samples.EnergyHub/Trax.Samples.EnergyHub.csproj
<Project Sdk="Microsoft.NET.Sdk">
<!-- ... -->
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" />
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" />
    <ProjectReference Include="../../../../Trax.Effect/src/Trax.Effect/Trax.Effect.csproj" />
    <ProjectReference Include="../../../../Trax.Effect/src/Trax.Effect.Data.Postgres/Trax.Effect.Data.Postgres.csproj" />
    <ProjectReference Include="../../../../Trax.Mediator/src/Trax.Mediator/Trax.Mediator.csproj" />
    <ProjectReference Include="../../../../Trax.Scheduler/src/Trax.Scheduler/Trax.Scheduler.csproj" />
  </ItemGroup>
</Project>
```

The samples build against the Trax projects in the repository, so they use `ProjectReference`
where an application uses a `PackageReference` at the release version. The library's code uses
nothing from `Trax.Effect.Data.Postgres`; the reference is there so both executables get
`UsePostgres` through it. A library that leaves the data provider out works as well, with each
executable referencing it instead.

Each executable is a `Microsoft.NET.Sdk.Web` project that references the library and adds the
packages for what that process does. The worker needs only the effect providers and the RabbitMQ
broadcaster:

```xml from=Trax.Samples/samples/DistributedWorkers/Trax.Samples.EnergyHub.Worker/Trax.Samples.EnergyHub.Worker.csproj
<Project Sdk="Microsoft.NET.Sdk.Web">
<!-- ... -->
  <ItemGroup>
    <ProjectReference Include="..\Trax.Samples.EnergyHub\Trax.Samples.EnergyHub.csproj" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../../../Trax.Effect/src/Trax.Effect.Provider.Json/Trax.Effect.Provider.Json.csproj" />
    <ProjectReference Include="../../../../Trax.Effect/src/Trax.Effect.Provider.Parameter/Trax.Effect.Provider.Parameter.csproj" />
    <ProjectReference Include="../../../../Trax.Effect/src/Trax.Effect.JunctionProvider.Progress/Trax.Effect.JunctionProvider.Progress.csproj" />
    <ProjectReference Include="../../../../Trax.Effect/src/Trax.Effect.Broadcaster.RabbitMQ/Trax.Effect.Broadcaster.RabbitMQ.csproj" />
  </ItemGroup>
</Project>
```

The hub adds the dashboard, Trax.Api, its API key scheme and Trax.Api.GraphQL on top of the same
library. In each executable's `Program.cs`, `AddMediator` names an assembly the library owns, so
the train bus scans the library rather than the host:

```csharp from=Trax.Samples/samples/DistributedWorkers/Trax.Samples.EnergyHub.Hub/Program.cs
        .AddMediator(typeof(ManifestNames).Assembly)
```

Pin exact Trax versions, once for the solution in `Directory.Packages.props`, as the templates do:
a floating `Version="1.*"` restores whatever was published last, and a minor release can change an
API the trains call. Every Trax package releases at one version.

How these executables can be arranged across processes, and which sample runs each arrangement, is
in [Deployment topologies](/docs/reference/deployment-topologies).

Contributing to Trax itself? The repository's own layout is in
[Repository layout](/docs/contributing/repository-layout).
