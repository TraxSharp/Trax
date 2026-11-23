using System.Globalization;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Api.StateMachine.E2E;

public record Mesh0Output
{
    public string Name { get; init; } = "";
    public Mesh1Output[] Mesh1s { get; init; } = [];
    public Mesh2Output[] Mesh2s { get; init; } = [];
    public Mesh3Output[] Mesh3s { get; init; } = [];
    public Mesh4Output[] Mesh4s { get; init; } = [];
    public Mesh5Output[] Mesh5s { get; init; } = [];
    public Mesh6Output[] Mesh6s { get; init; } = [];
}
