using System.Globalization;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using HotChocolate.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Trax.Api.Auth.ApiKey;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Extensions;
using Trax.Api.GraphQL.PersistedOperations;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E;

/// <summary>What an <see cref="E2EHost"/> adds to the default host.</summary>
public sealed record E2EHostOptions
{
    /// <summary>Configures the state machines: the generic opt-in, the TTL, the sweeps.</summary>
    public Action<StateMachineOptions>? Machines { get; init; }

    /// <summary>
    /// The fakes the host serves through their typed operations: each is registered as its subclass marked
    /// <c>[TraxMachine]</c> (<see cref="E2EMachines"/>). Every other machine of the assembly is registered unmarked.
    /// </summary>
    public IReadOnlyList<Type> Served { get; init; } = [];

    /// <summary>
    /// Machines the host registers as instances, before <c>AddTraxGraphQL</c> reads them: a machine served from its IR
    /// has no type of its own to name in <see cref="Served"/>.
    /// </summary>
    public IReadOnlyList<Trax.StateMachine.Persistence.IMachine> Instances { get; init; } = [];

    /// <summary>The views of machines served from features, registered before <c>AddTraxGraphQL</c> reads them.</summary>
    internal Trax.StateMachine.Persistence.FromIr.MachineViewCatalog? Views { get; init; }

    /// <summary>Applied to the GraphQL builder after the operations surface, when that is exposed.</summary>
    public Func<TraxGraphQLBuilder, TraxGraphQLBuilder>? GraphQL { get; init; }

    /// <summary>
    /// Exposes the operations queries and mutations, gated to <see cref="E2EHost.OperatorRole"/>, over the
    /// shared operations service. With it on, every train's lifecycle is published to the operator.
    /// </summary>
    public bool ExposeOperations { get; init; }

    /// <summary>
    /// When set, only the trains it keeps are discovered: a host gated with <c>RequireAuthorization(...)</c> leaves
    /// out the assembly's <c>[TraxAllowAnonymous]</c> trains, which such a host refuses to start with.
    /// </summary>
    public Func<TrainRegistration, bool>? Trains { get; init; }

    /// <summary>Registrations made after Trax's, so the last one wins.</summary>
    public Action<IServiceCollection>? Services { get; init; }

    /// <summary>Endpoints mapped beside the GraphQL endpoint, such as the operations console.</summary>
    public Action<IEndpointRouteBuilder>? Endpoints { get; init; }

    /// <summary>Stores every run's input and output on its row (<c>SaveTrainParameters</c>), as most hosts do.</summary>
    public bool SaveTrainParameters { get; init; }

    /// <summary>The <c>application_name</c> of the host's database sessions.</summary>
    public string? ApplicationName { get; init; }

    /// <summary>
    /// Authentication schemes registered beside the API keys, before the GraphQL endpoint, as a host registers its
    /// schemes: a JWT scheme, for the tests about a socket a token authenticated.
    /// </summary>
    public Action<IServiceCollection>? Authentication { get; init; }

    /// <summary>A host that serves <see cref="WizardMachine"/> through its typed operations.</summary>
    public static E2EHostOptions Wizard() => new() { Served = [typeof(WizardMachine)] };

    /// <summary>The policy <see cref="GenericOptIn"/> gates the generic mutations with: any signed-in caller.</summary>
    public const string GenericPolicy = "sm-e2e-generic";

    /// <summary>Opts in to the generic <c>stateMachine</c> mutations behind <see cref="GenericPolicy"/>.</summary>
    public static Action<StateMachineOptions> OptInToGeneric { get; } =
        o => o.ExposeGenericStateMachineMutations(g => g.Policy = GenericPolicy);

    /// <summary>Registers <see cref="GenericPolicy"/>, which any authenticated caller satisfies.</summary>
    public static void AddGenericPolicy(IServiceCollection services) =>
        services.AddAuthorization(o =>
            o.AddPolicy(GenericPolicy, p => p.RequireAuthenticatedUser())
        );

    /// <summary>A host that serves the generic mutations to any signed-in caller.</summary>
    public static E2EHostOptions GenericOptIn() =>
        new() { Machines = OptInToGeneric, Services = AddGenericPolicy };
}
