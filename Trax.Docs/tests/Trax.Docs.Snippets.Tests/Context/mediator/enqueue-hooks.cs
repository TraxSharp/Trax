// What the compiled example on mediator/enqueue-hooks.md takes as given.
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

public record ProcessMatchResultInput(string MatchId, string Region);

public record ProcessMatchResultOutput(string MatchId);

public interface IProcessMatchResultTrain
    : IServiceTrain<ProcessMatchResultInput, ProcessMatchResultOutput>;

public class MatchRecord
{
    public long Id { get; set; }

    public string MatchId { get; set; } = "";

    public string Region { get; set; } = "";
}

public class GameDbContext(DbContextOptions<GameDbContext> options) : DbContext(options)
{
    public DbSet<MatchRecord> Matches => Set<MatchRecord>();
}

public class RecordMatchResultJunction
    : EffectJunction<ProcessMatchResultInput, ProcessMatchResultOutput>
{
    public override Task<ProcessMatchResultOutput> Run(ProcessMatchResultInput input) =>
        Task.FromResult(new ProcessMatchResultOutput(input.MatchId));
}
