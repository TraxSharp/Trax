// What the examples on sdk-reference/graphql-api/query-models.md take as given: the loaders the
// resolver examples batch through, and the review type the ExposeAs example hides.
public record Achievement(long PlayerId, string Name);

public class AchievementLoader
{
    public Task<IReadOnlyList<Achievement>> LoadAsync(long playerId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Achievement>>([]);
}

public record Team(long Id, string Name);

public class TeamLoader
{
    public Task<Team?> LoadAsync(long teamId, CancellationToken ct) => Task.FromResult<Team?>(null);
}

public class Review
{
    public int Id { get; set; }
}
