---
title: "2.2 Add a solution and a test project"
description: "Chapter 2 of the Trax book, section 2: Add a solution and a test project."
type: tutorial
area: general
parent: "2. Testing PlaceOrder"
grand_parent: The Trax book
nav_order: 2
snippet_context: none
follows: book/testing-place-order/three-kinds-of-test
---

# 2.2 Add a solution and a test project

The tests live in a project of their own under `tests/`, which chapter 1's project file already
leaves out of the application. A solution file at the root holds both projects:

```bash
dotnet new sln -n Shopfront
mkdir -p tests/Shopfront.Tests
```

Add the test packages to `Directory.Packages.props`, beside the Trax ones:

```xml file=Directory.Packages.props
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Trax.Core" Version="1.61.0" />
    <PackageVersion Include="Trax.Core.Testing" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Data.InMemory" Version="1.61.0" />
    <PackageVersion Include="Trax.Effect.Provider.Parameter" Version="1.61.0" />
    <PackageVersion Include="Trax.Mediator" Version="1.61.0" />
    <PackageVersion Include="Trax.Mediator.Testing" Version="1.61.0" />
  </ItemGroup>

  <ItemGroup>
    <PackageVersion Include="AwesomeAssertions" Version="9.6.0" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
    <PackageVersion Include="NUnit" Version="5.0.0" />
    <PackageVersion Include="NUnit.Analyzers" Version="4.15.0" />
    <PackageVersion Include="NUnit3TestAdapter" Version="6.3.0" />
  </ItemGroup>
</Project>
```

`tests/Shopfront.Tests/Shopfront.Tests.csproj`:

```xml file=tests/Shopfront.Tests/Shopfront.Tests.csproj
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\Shopfront.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="AwesomeAssertions" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="NUnit" />
    <PackageReference Include="NUnit.Analyzers" />
    <PackageReference Include="NUnit3TestAdapter" />
    <PackageReference Include="Trax.Core.Testing" />
    <PackageReference Include="Trax.Mediator.Testing" />
  </ItemGroup>
</Project>
```

Then add both projects to the solution:

```bash
dotnet sln add Shopfront.csproj tests/Shopfront.Tests/Shopfront.Tests.csproj
```

The junctions and the types they pass each other are `internal`, so nothing in the application
outside the train can depend on them. The tests are the one exception, and the application says
so. Add one line to `Shopfront.csproj`:

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
    <!-- The junctions are internal; their tests are the one project that may see them. -->
    <InternalsVisibleTo Include="Shopfront.Tests" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Trax.Core" />
    <PackageReference Include="Trax.Effect" />
    <PackageReference Include="Trax.Effect.Data.InMemory" />
    <PackageReference Include="Trax.Effect.Provider.Parameter" />
    <PackageReference Include="Trax.Mediator" />
  </ItemGroup>
</Project>
```

`tests/Shopfront.Tests/GlobalUsings.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/GlobalUsings.cs
global using AwesomeAssertions;
global using NUnit.Framework;
```

By the end of the chapter the test project looks like this:

```text
tests/
└── Shopfront.Tests/
    ├── Shopfront.Tests.csproj
    ├── GlobalUsings.cs
    ├── ArchitectureGuards.cs
    ├── UnitTests/
    │   ├── PriceCartJunctionTests.cs
    │   ├── ReserveStockJunctionTests.cs
    │   └── TakePaymentJunctionTests.cs
    └── IntegrationTests/
        ├── PlaceOrderTrainTests.cs
        └── HostTests.cs
```

Test names say what is called, in what situation, and what should happen:
`Run_EmptyCart_Throws`.

## SDK Reference

> [ITrainBus](/docs/sdk-reference/mediator-api/i-train-bus) | [UseInMemory](/docs/sdk-reference/configuration/use-in-memory) | [SaveTrainParameters](/docs/sdk-reference/configuration/save-train-parameters) | [Metadata](/docs/effect/metadata) | [Mediator testing](/docs/sdk-reference/testing/mediator-testing) | [Architecture guards](/docs/reference/architecture-guards)

---

Next: [2.3 Test a junction](/docs/book/testing-place-order/test-a-junction)
