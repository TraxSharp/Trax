---
title: "9.6 Test it"
description: "Chapter 9 of the Trax book, section 6: Test it."
type: tutorial
area: general
parent: "9. Scheduled work"
grand_parent: The Trax book
nav_order: 6
snippet_context: none
follows: book/scheduled-work/run-it-now
---

# 9.6 Test it

The junctions are tested against the test database, as the others are. The stock report reads the
clock from a `TimeProvider`, so the test hands it a `FakeTimeProvider`, a clock the test sets by
hand. It comes from the `Microsoft.Extensions.TimeProvider.Testing` package, which the test project
needs from here on. `Directory.Packages.props` gains its version:

```xml file=Directory.Packages.props
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup>
    <PackageVersion Include="Trax.Api.Auth.Jwt" Version="1.69.0" />
    <PackageVersion Include="Trax.Api.Dashboard" Version="1.69.0" />
    <PackageVersion Include="Trax.Api.GraphQL" Version="1.69.0" />
    <PackageVersion Include="Trax.Core" Version="1.69.0" />
    <PackageVersion Include="Trax.Core.Testing" Version="1.69.0" />
    <PackageVersion Include="Trax.Effect" Version="1.69.0" />
    <PackageVersion Include="Trax.Effect.Data.InMemory" Version="1.69.0" />
    <PackageVersion Include="Trax.Effect.Data.Postgres" Version="1.69.0" />
    <PackageVersion Include="Trax.Effect.Data.Testing" Version="1.69.0" />
    <PackageVersion Include="Trax.Effect.JunctionProvider.Logging" Version="1.69.0" />
    <PackageVersion Include="Trax.Effect.Provider.Parameter" Version="1.69.0" />
    <PackageVersion Include="Trax.Mediator" Version="1.69.0" />
    <PackageVersion Include="Trax.Mediator.Testing" Version="1.69.0" />
    <PackageVersion Include="Trax.Scheduler" Version="1.69.0" />
  </ItemGroup>

  <ItemGroup>
    <PackageVersion Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.3" />
  </ItemGroup>

  <ItemGroup>
    <PackageVersion Include="AwesomeAssertions" Version="9.6.0" />
    <PackageVersion Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.12" />
    <PackageVersion Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.10.0" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
    <!-- NUnit3TestAdapter pulls these at versions deprecated as Legacy; pin newer ones. -->
    <PackageVersion Include="Microsoft.Testing.Extensions.VSTestBridge" Version="2.5.1" />
    <PackageVersion Include="Microsoft.Testing.Platform.MSBuild" Version="2.5.1" />
    <PackageVersion Include="NUnit" Version="5.0.0" />
    <PackageVersion Include="NUnit.Analyzers" Version="4.15.0" />
    <PackageVersion Include="NUnit3TestAdapter" Version="6.3.0" />
  </ItemGroup>
</Project>
```

and the test project references it:

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
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="NUnit" />
    <PackageReference Include="NUnit.Analyzers" />
    <PackageReference Include="NUnit3TestAdapter" />
    <PackageReference Include="Trax.Core.Testing" />
    <PackageReference Include="Trax.Effect.Data.InMemory" />
    <PackageReference Include="Trax.Effect.Data.Testing" />
    <PackageReference Include="Trax.Mediator.Testing" />
  </ItemGroup>
</Project>
```

`tests/Shopfront.Tests/UnitTests/StockJunctionTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/UnitTests/StockJunctionTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shopfront.Staff;
using Shopfront.Trains.LowStockCheck;
using Shopfront.Trains.LowStockCheck.Junctions;
using Shopfront.Trains.StockReport;
using Shopfront.Trains.StockReport.Junctions;
using Shopfront.Trains.StockReport.Models;

namespace Shopfront.Tests.UnitTests;

[TestFixture]
public class StockJunctionTests
{
    [SetUp]
    public Task ResetTheDatabase() => TestDatabase.Reset();

    private sealed class RecordingNotifier : IStaffNotifier
    {
        public List<string> Messages { get; } = [];

        public Task Notify(string message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    [Test]
    public async Task CountStock_CountsEveryProduct()
    {
        await using var db = TestDatabase.NewContext();

        var report = await new CountStockJunction(
            db,
            NullLogger<CountStockJunction>.Instance,
            new FakeTimeProvider()
        ).Run(new StockReportInput());

        report
            .Lines.Should()
            .Equal(new StockLine("CAP", 0), new StockLine("MUG", 10), new StockLine("TEE", 3));
        report.Units.Should().Be(13);
    }

    [Test]
    public async Task FindLowStock_ListsProductsAtOrBelowTheThreshold()
    {
        await using var db = TestDatabase.NewContext();

        var low = await new FindLowStockJunction(db).Run(new LowStockCheckInput { Threshold = 3 });

        low.LowSkus.Should().Equal("CAP", "TEE");
    }

    [Test]
    public async Task NotifyStaff_NamesTheLowProducts()
    {
        var staff = new RecordingNotifier();

        await new NotifyStaffJunction(staff).Run(new LowStockCheckOutput(["CAP", "TEE"]));

        staff.Messages.Should().Equal("Low on CAP, TEE.");
    }

    [Test]
    public async Task NotifyStaff_NothingLow_SaysNothing()
    {
        var staff = new RecordingNotifier();

        await new NotifyStaffJunction(staff).Run(new LowStockCheckOutput([]));

        staff.Messages.Should().BeEmpty();
    }
}
```

The host test checks what the host declares: both manifests, the check after the report.

`tests/Shopfront.Tests/IntegrationTests/ScheduleTests.cs`:

```csharp compile=tests file=tests/Shopfront.Tests/IntegrationTests/ScheduleTests.cs
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shopfront.Auth;
using Trax.Core.Extensions;

namespace Shopfront.Tests.IntegrationTests;

[TestFixture]
public class ScheduleTests
{
    private WebApplicationFactory<Program>? _app;

    [SetUp]
    public async Task StartTheApplication()
    {
        await TestDatabase.Reset();
        _app = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.UseEnvironment("Development")
                .UseSetting("ConnectionStrings:Shopfront", TestDatabase.ConnectionString)
        );
    }

    [TearDown]
    public async ValueTask StopTheApplication()
    {
        if (_app is not null)
            await _app.DisposeAsync().ConfigureAwait(false);
    }

    [Test]
    public async Task Manifests_TheReportAndTheCheckAreScheduled()
    {
        var client = _app.NotNull().CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            DevJwt.Mint("lin", Roles.Staff, TimeProvider.System)
        );

        var response = await client.Post(
            "{ operations { manifests(take: 10) { items { externalId cronExpression } } } }"
        );

        var manifests = response
            .GetProperty("data")
            .GetProperty("operations")
            .GetProperty("manifests")
            .GetProperty("items")
            .EnumerateArray()
            .ToDictionary(
                m => m.GetProperty("externalId").GetString().NotNull(),
                m => m.GetProperty("cronExpression").GetString(),
                StringComparer.Ordinal
            );
        manifests
            .Should()
            .ContainKey(ManifestNames.StockReport)
            .WhoseValue.Should()
            .Be("0 2 * * *");
        manifests.Should().ContainKey(ManifestNames.LowStockCheck);
    }
}
```

```bash
dotnet test
```

```text
Passed!  - Failed:     0, Passed:    60, Skipped:     0, Total:    60, Duration: 55 s - Shopfront.Tests.dll (net10.0)
```

## What we have

Shopfront counts its stock every night and tells staff what is running low, without anyone asking,
and leaves the shop's holidays alone. Staff can see each schedule and each run in the console, and
run either now.

Each of those runs succeeded. The next chapter adds work that fails: a supplier's stock feed that
is sometimes down, retried, given up on, and picked up again by staff.

## SDK Reference

> [AddScheduler](/docs/sdk-reference/scheduler-api/add-scheduler) | [Schedule](/docs/sdk-reference/scheduler-api/schedule) | [Scheduling helpers](/docs/sdk-reference/scheduler-api/scheduling-helpers) | [Exclusions](/docs/scheduler/exclusions) | [Scheduling options](/docs/scheduler/scheduling-options) | [Dependent trains](/docs/scheduler/dependent-trains)

---

Next: [10. Failures, retries and dead letters](/docs/book/failures-retries-and-dead-letters)
