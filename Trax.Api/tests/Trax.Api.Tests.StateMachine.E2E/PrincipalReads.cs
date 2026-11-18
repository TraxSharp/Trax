using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using HotChocolate.Types;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Trax.Api.Auth;
using Trax.Api.Auth.Jwt;
using Trax.Api.Auth.Jwt.Testing;
using Trax.Api.StateMachine.E2E.Fakes;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// Counts how often a node reads the caller's user key for the state machines, which every check of an owner-only
/// rule does.
/// </summary>
public sealed class PrincipalReads
{
    private int _count;

    /// <summary>Reads so far.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>
    /// Replaces the host's <see cref="ISnapshotPrincipal"/> with one that counts its reads, and registers the
    /// counter.
    /// </summary>
    public static void Install(IServiceCollection services)
    {
        var reads = new PrincipalReads();
        services.AddSingleton(reads);
        services.AddScoped<ISnapshotPrincipal>(sp => new Counting(
            new TraxCallerSnapshotPrincipal(sp.GetRequiredService<TraxCaller>()),
            reads
        ));
    }

    private sealed class Counting(ISnapshotPrincipal inner, PrincipalReads reads)
        : ISnapshotPrincipal
    {
        public string? CurrentUserKey
        {
            get
            {
                Interlocked.Increment(ref reads._count);
                return inner.CurrentUserKey;
            }
        }
    }
}
