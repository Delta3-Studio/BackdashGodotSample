namespace SpaceWar.Services;

using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Backdash.JsonConverters;
using Models;
using HttpClient = System.Net.Http.HttpClient;

public sealed class LobbyHttpClient(int localPort, Uri serverUri, int gameId)
{
    static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter(),
            new JsonIPAddressConverter(),
            new JsonIPEndPointConverter(),
        }
    };

    readonly HttpClient client = new()
    {
        BaseAddress = serverUri,
    };

    readonly Guid recreationKey = Guid.NewGuid();

    public async Task<User> EnterLobby(
        string lobbyName, string username, PlayerMode mode,
        CancellationToken ct = default
    )
    {
        var localEndpoint = await GetLocalEndpoint(ct);
        var response = await client.PostAsJsonAsync($"/{gameId}/lobby", new
        {
            lobbyName,
            username,
            mode,
            localEndpoint,
            recreationKey,
        }, jsonOptions, ct);

        if (response.StatusCode is HttpStatusCode.UnprocessableEntity)
            throw new InvalidOperationException("Failed to enter lobby");

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<User>(jsonOptions, ct)
                     ?? throw new InvalidOperationException();

        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("token", result.Token.ToString());
        return result;
    }

    public async Task<Lobby> GetLobby(CancellationToken ct = default) =>
        await client.GetFromJsonAsync<Lobby>("/entry/lobby", jsonOptions, ct)
        ?? throw new InvalidOperationException();

    public async Task LeaveLobby(CancellationToken ct = default)
    {
        var response = await client.DeleteAsync("/entry", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task ToggleReady(CancellationToken ct = default)
    {
        var response = await client.PutAsync("/entry/ready", null, ct);
        response.EnsureSuccessStatusCode();
    }

    async Task<IPEndPoint> GetLocalEndpoint(CancellationToken ct = default)
    {
        try
        {
            using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            await socket.ConnectAsync("8.8.8.8", 65530, ct);
            if (socket.LocalEndPoint is not IPEndPoint { Address: { } ipAddress })
                return null;

            return new(ipAddress, localPort);
        }
        catch (Exception)
        {
            // skip
        }

        return null;
    }
}
