using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine.Features;
using Trax.StateMachine.Persistence.FromIr;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>A train whose subject is its input: the member the machine's owner is.</summary>
public sealed record BorrowFromIrInput(string Source, string MemberId);
