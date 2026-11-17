using Microsoft.Extensions.DependencyInjection;
using Trax.Mediator.Services.TrainAuthorization;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrustedExecution;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>One authorization the mediator asked for: which train, for whom, and whether the scope was trusted.</summary>
public sealed record AuthorizationCall(string Train, string? User, bool Trusted);
