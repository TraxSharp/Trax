namespace Trax.Samples.GameServer;

/// <summary>
/// GraphQL namespace constants for the game server API.
/// Trains and query models sharing a namespace are grouped under the same
/// sub-field in the schema (e.g. <c>discover { players { lookupPlayer } }</c>).
/// </summary>
public static class GraphQLNamespaces
{
    public const string Players = "players";
    public const string Matches = "matches";
    public const string Leaderboard = "leaderboard";
}
