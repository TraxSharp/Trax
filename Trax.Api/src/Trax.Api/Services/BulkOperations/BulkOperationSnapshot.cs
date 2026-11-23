using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.DTOs;
using Trax.Api.Services.Lists;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Api.Services.BulkOperations;

/// <summary>
/// The ids a bulk operation will attempt, in the order it attempts them, and the exclusive end
/// index of each batch.
/// </summary>
internal sealed record BulkOperationSnapshot(long[] Ids, int[] BatchEnds);
