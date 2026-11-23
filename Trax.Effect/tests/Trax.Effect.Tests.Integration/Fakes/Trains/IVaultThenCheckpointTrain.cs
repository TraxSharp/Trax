using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Effect.Attributes;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.Fakes.Trains;

/// <summary>
/// Routes on a source and then on a sensitive vault, and checkpoints after both: the vault's track
/// is left out of the stored routes.
/// </summary>
public interface IVaultThenCheckpointTrain : IServiceTrain<string, string>;
