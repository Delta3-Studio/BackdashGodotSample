using System.Buffers;
using Backdash.Network;

namespace SpaceWar.Services;

using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using Backdash.Network.Client;
using Models;

public sealed class LobbyUdpClient : IDisposable
{
    readonly IPEndPoint serverEndpoint;
    readonly UdpSocket socket;
    readonly CancellationTokenSource cts = new();
    readonly HashSet<Guid> knownClients = [];
    readonly Lock locker = new();
    bool disposed;

    public LobbyUdpClient(int localPort, Uri serverUrl, int serverPort)
    {
        var serverAddress = NetUtils.GetDnsIpAddress(serverUrl.DnsSafeHost);
        serverEndpoint = new(serverAddress, serverPort);
        socket = new(localPort);
        Task.Run(() => Receive(cts.Token));
    }

    public async Task HandShake(User user, CancellationToken ct = default)
    {
        var sendBuffer = ArrayPool<byte>.Shared.Rent(Unsafe.SizeOf<Guid>());

        try
        {
            if (!user.Token.TryWriteBytes(sendBuffer, true, out var bytesWritten) || bytesWritten is 0) return;
            await socket.SendToAsync(sendBuffer.AsMemory()[..bytesWritten], serverEndpoint, ct).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sendBuffer);
        }
    }

    public async Task HandShake(User user, Peer[] peers, CancellationToken ct = default)
    {
        var sendBuffer = ArrayPool<byte>.Shared.Rent(Unsafe.SizeOf<Guid>());

        try
        {
            if (peers.Length is 0 ||
                !user.PeerId.TryWriteBytes(sendBuffer, true, out var bytesWritten) || bytesWritten is 0)
                return;

            ReadOnlyMemory<byte> msgBytes = sendBuffer.AsMemory()[..bytesWritten];
            foreach (var peer in peers)
            {
                if (peer.Connected && peer.PeerId != user.PeerId)
                    await socket.SendToAsync(msgBytes, peer.GetEndpointForUser(user), ct).ConfigureAwait(false);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sendBuffer);
        }
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    async ValueTask Receive(CancellationToken stoppingToken)
    {
        var idSize = Unsafe.SizeOf<Guid>();
        var recBuffer = GC.AllocateArray<byte>(idSize, pinned: true);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var receiveInfo = await socket
                    .ReceiveAsync(recBuffer, stoppingToken)
                    .ConfigureAwait(false);

                if (receiveInfo.ReceivedBytes is 0) continue;

                if (receiveInfo.ReceivedBytes != idSize)
                {
                    var msg = Encoding.UTF8.GetString(recBuffer.AsSpan(0, receiveInfo.ReceivedBytes));
                    Log.Debug($"recv ({receiveInfo.RemoteEndPoint}): '{msg}'");
                    continue;
                }

                Guid peerToken = new(recBuffer.AsSpan(0, receiveInfo.ReceivedBytes), true);
                Log.Debug($"recv ({receiveInfo.RemoteEndPoint}): Ping from '{peerToken}'");
                if (peerToken == Guid.Empty) continue;
                lock (locker) knownClients.Add(peerToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // skip
            }
        }
    }

    public bool IsKnown(Guid id)
    {
        lock (locker)
            return knownClients.Contains(id);
    }

    public void Stop()
    {
        if (!cts.IsCancellationRequested)
            cts.Cancel();

        socket.Close();
    }

    public void Dispose()
    {
        Stop();
        if (disposed) return;
        disposed = true;
        socket.Dispose();
        cts.Dispose();
    }
}
