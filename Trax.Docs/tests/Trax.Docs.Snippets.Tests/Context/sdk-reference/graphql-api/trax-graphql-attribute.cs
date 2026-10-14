// What the examples on sdk-reference/graphql-api/trax-graphql-attribute.md take as given: the
// inputs, outputs, interfaces and junctions of the trains they expose.
public record LookupPlayerInput(string PlayerId);

public record LookupPlayerOutput(string PlayerId, int Rank, int Wins, int Losses, int Rating);

public interface ILookupPlayerTrain : IServiceTrain<LookupPlayerInput, LookupPlayerOutput>;

public class FetchPlayerJunction : Junction<LookupPlayerInput, LookupPlayerOutput>
{
    public override Task<LookupPlayerOutput> Run(LookupPlayerInput input) =>
        Task.FromResult(new LookupPlayerOutput(input.PlayerId, 1, 0, 0, 1500));
}

public record BanPlayerInput(string PlayerId, string Reason);

public interface IBanPlayerTrain : IServiceTrain<BanPlayerInput, Unit>;

public class ApplyBanJunction : Junction<BanPlayerInput, Unit>
{
    public override Task<Unit> Run(BanPlayerInput input) => Task.FromResult(Unit.Default);
}

public record PingInput;

public record PongOutput(DateTime At);

public interface IPingTrain : IServiceTrain<PingInput, PongOutput>;

public class PingJunction : Junction<PingInput, PongOutput>
{
    public override Task<PongOutput> Run(PingInput input) =>
        Task.FromResult(new PongOutput(DateTime.UtcNow));
}

public record RecalculateLeaderboardInput;

public interface IRecalculateLeaderboardTrain : IServiceTrain<RecalculateLeaderboardInput, Unit>;

public class AggregateScoresJunction : Junction<RecalculateLeaderboardInput, Unit>
{
    public override Task<Unit> Run(RecalculateLeaderboardInput input) =>
        Task.FromResult(Unit.Default);
}

public class RankPlayersJunction : Junction<Unit, Unit>
{
    public override Task<Unit> Run(Unit input) => Task.FromResult(Unit.Default);
}
