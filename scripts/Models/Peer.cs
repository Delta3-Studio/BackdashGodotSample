using System.Diagnostics.CodeAnalysis;

namespace SpaceWar.Models;

using System.Net;

public sealed class Peer
{
    public required Guid PeerId { get; init; }
    public required string Username { get; init; }
    public required IPEndPoint Endpoint { get; init; }
    public IPEndPoint LocalEndpoint { get; init; }
    public bool Connected { get; init; }
    public bool Ready { get; init; }

    [MemberNotNullWhen(true, nameof(LocalEndpoint))]
    public bool InSameNetwork(User user) => Equals(Endpoint.Address, user.IP) && LocalEndpoint is not null;

    public IPEndPoint GetEndpointForUser(User user) => InSameNetwork(user) ? LocalEndpoint : Endpoint;
}
