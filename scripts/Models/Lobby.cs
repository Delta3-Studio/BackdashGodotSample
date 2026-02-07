namespace SpaceWar.Models;

public sealed class Lobby
{
    public required string Name { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public bool Ready { get; init; }
    public required Peer[] Players { get; init; }
    public required Peer[] Spectators { get; init; }
    public required SpectatorMap[] SpectatorMapping { get; init; }

    public Peer[] GetPeers(PeerMode mode) =>
        mode switch
        {
            PeerMode.Player => Players,
            PeerMode.Spectator => Spectators,
            _ => throw new ArgumentOutOfRangeException(),
        };

    public sealed record SpectatorMap(Guid Host, Guid[] Watchers);
}
