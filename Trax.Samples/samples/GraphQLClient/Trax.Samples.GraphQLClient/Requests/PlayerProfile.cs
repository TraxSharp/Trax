namespace Trax.Samples.GraphQLClient.Requests;

/// <summary>
/// Shared response DTO used by all three modes (A, E, D). When the three modes converge on
/// the same answer, comparing the deserialized instance is meaningful: <c>resultA == resultE
/// == resultD</c> is the contract that proves the modes are interchangeable.
/// </summary>
public sealed record PlayerProfile(
    string Id,
    string Name,
    int? Level,
    string Rank,
    GuildSummary? Guild,
    IReadOnlyList<ItemSummary> Inventory
);

public sealed record GuildSummary(string Id, string Name);

public sealed record ItemSummary(string Id, string Name, string Category);
