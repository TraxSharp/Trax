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
│       └── Junctions/
│           └── <Step>Junction.cs    one class per step
└── tests/
    └── MyCompany.Hub.Tests/    UnitTests/ for junctions, IntegrationTests/ for trains and the host
```

`AddMediator(typeof(Program).Assembly)` registers every train in the assembly, wherever it sits,
so adding a train is adding its folder. When the trains outgrow the host, move `Trains/` into a
class library, reference it from the host, and name both assemblies in `AddMediator`; a train in
an assembly `AddMediator` does not scan is not registered. A second process (a separate scheduler
or worker) references the same library and points at the same database. The
[Samples](/docs/samples) show those splits.

Contributing to Trax itself? The repository's own layout is in
[Repository layout](/docs/contributing/repository-layout).
