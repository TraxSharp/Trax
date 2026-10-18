---
title: "1.2 Create the project"
description: "Chapter 1 of the Trax book, section 2: Create the project."
type: tutorial
area: general
parent: "1. An order is a train"
grand_parent: The Trax book
nav_order: 2
snippet_context: none
follows: book/an-order-is-a-train/requirements
---

# 1.2 Create the project

```bash
dotnet new web -n Shopfront
cd Shopfront
```

Package versions live in one place, `Directory.Packages.props`, beside the project file. Create it:

```xml file=Directory.Packages.props
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Trax.Core" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Data.InMemory" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Provider.Parameter" Version="1.61.0" />
    <PackageVersion Include="Trax.Mediator" Version="1.61.0" />
  </ItemGroup>
</Project>
```

Every Trax package releases at the same version, so they are pinned together, to an exact version.
Then replace `Shopfront.csproj` with this:

```xml file=Shopfront.csproj
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <!-- The tests live under tests/, in a project of their own. -->
    <DefaultItemExcludes>$(DefaultItemExcludes);tests/**</DefaultItemExcludes>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Trax.Core" />
    <PackageReference Include="Trax.Effect" />
    <PackageReference Include="Trax.Effect.Data.InMemory" />
    <PackageReference Include="Trax.Effect.Provider.Parameter" />
    <PackageReference Include="Trax.Mediator" />
  </ItemGroup>
</Project>
```

`Trax.Core` holds the chain and the types it returns, `Trax.Effect` holds trains and junctions,
`Trax.Effect.Data.InMemory` keeps a record of every run in memory, `Trax.Effect.Provider.Parameter`
writes each run's input and output into that record, and `Trax.Mediator` finds our trains and
hands them to whoever asks.

By the end of the chapter the project looks like this:

```text
Shopfront/
├── Directory.Packages.props
├── Shopfront.csproj
├── Program.cs
├── Inventory/
│   └── Catalogue.cs
├── Payments/
│   ├── IPaymentProvider.cs
│   └── FakePaymentProvider.cs
└── Trains/
    └── PlaceOrder/
        ├── PlaceOrderInput.cs
        ├── PlaceOrderOutput.cs
        ├── IPlaceOrderTrain.cs
        ├── PlaceOrderTrain.cs
        ├── Junctions/
        │   ├── PriceCartJunction.cs
        │   ├── ReserveStockJunction.cs
        │   ├── TakePaymentJunction.cs
        │   └── ConfirmOrderJunction.cs
        └── Models/
            ├── OrderLine.cs
            ├── PricedCart.cs
            ├── PricedLine.cs
            ├── StockReservation.cs
            └── Payment.cs
```

The tests go in `tests/`, which chapter 2 fills.

Each train gets a folder under `Trains/`. Its top level is the train's contract: the input, the
output, the interface and the train. Its junctions go in `Junctions/`, one file each, and every
other type it uses goes in `Models/`. The shop itself, what it sells and how it is paid, lives outside
`Trains/`, because more than one train will use it. [Organize trains in a
project](/docs/core/organize-trains) has the layout on its own.

## SDK Reference

> [Junctions](/docs/sdk-reference/train-methods/junctions) | [Chain](/docs/sdk-reference/train-methods/chain) | [Run / RunEither](/docs/sdk-reference/train-methods/run) | [TrainException](/docs/sdk-reference/trains-and-junctions/train-exception) | [AddTrax / AddEffects](/docs/sdk-reference/configuration) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [AddMediator](/docs/sdk-reference/configuration/add-mediator)

---

Next: [1.3 The shop](/docs/book/an-order-is-a-train/the-shop)
